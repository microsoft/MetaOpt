from .abstract_partitioning_method import AbstractPartitioningMethod
import numpy as np
import networkx as nx
import time


# Randomly partitions the graph, but ensures that each subgraph is contiguous
class LeaderElectionUniform(AbstractPartitioningMethod):
    def __init__(self, num_partitions=None, seed=0, tolerance=0.2):
        super().__init__(num_partitions=num_partitions, weighted=False)
        self.seed = seed
        self.tolerance = 1 + tolerance

    @property
    def name(self):
        return "leader_election_uniform"

    def _partition_impl(self, G, topo_name=""):
        if not hasattr(self, "_num_partitions"):
            self._num_partitions = self._default_num_partitions(G)

        np.random.seed(self.seed)
        # First, select the "seed nodes" for our partitioning. Each seed node
        # represents a single partition. The remaining nodes will be assigned to
        # one of the seed nodes until every node is assigned
        start = time.time()
        seed_nodes = np.random.choice(G.nodes, self.num_partitions, replace=False)
        partition_vector = np.ones(len(G.nodes), dtype=np.int32) * -1
        partition_vector[seed_nodes] = np.arange(self.num_partitions)
        uniform_partition_num = len(G.nodes) // self.num_partitions

        # while there are still unassigned nodes
        partition_try_num = 0
        while np.sum(partition_vector == -1) != 0:
            # Select a node that has been unassigned
            new_node = np.random.choice(np.argwhere(partition_vector == -1).flatten())

            # From this node, collect all of the partitions that it neighbors
            # in the graph. If all of its neighbors have been unassigned, pick
            # a new node
            neighboring_partitions = np.unique(
                [
                    partition_vector[x]
                    for x in nx.all_neighbors(G, new_node)
                    if partition_vector[x] != -1 and np.sum(partition_vector == partition_vector[x]) <= self.tolerance * (uniform_partition_num + 1)
                ]
            )

            already_tried = []
            while len(neighboring_partitions) == 0 and len(already_tried) < np.sum(partition_vector == -1) - 1:
                already_tried.append(new_node)
                new_node = np.random.choice(
                    np.setdiff1d(
                        np.argwhere(partition_vector == -1).flatten(), already_tried
                    )
                )

                neighboring_partitions = np.unique(
                    [
                        partition_vector[x]
                        for x in nx.all_neighbors(G, new_node)
                        if partition_vector[x] != -1 and 
                            np.sum(partition_vector == partition_vector[x]) <= self.tolerance * (uniform_partition_num + 1)
                    ]
                )

            if len(neighboring_partitions) == 0:
                partition_try_num += 1
                print(f" try {partition_try_num} invalid partitioning...")
                seed_nodes = np.random.choice(G.nodes, self.num_partitions, replace=False)
                partition_vector = np.ones(len(G.nodes), dtype=np.int32) * -1
                partition_vector[seed_nodes] = np.arange(self.num_partitions)
                uniform_partition_num = len(G.nodes) // self.num_partitions
                continue
            # Assign the selected node to one of the partitions it neighbors
            partition_assignment = np.random.choice(neighboring_partitions)
            partition_vector[new_node] = partition_assignment
        self.runtime = time.time() - start

        assert np.sum(partition_vector == -1) == 0
        return partition_vector
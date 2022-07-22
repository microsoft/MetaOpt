import parse_and_convert_graphml
from partitioning.spectral_clustering import SpectralClustering
# from partitioning.fm_partitioning import FMPartitioning
from partitioning.leader_election import LeaderElection
from partitioning.leader_election_uniform import LeaderElectionUniform
import numpy as np
import os

topo_name_list = ["GtsCe", "Cogentco"]
num_partitions_list = [
    # 2, 
    # 5, 
    10, 
    12,
    15, 
    20, 
    50
]

log_dir = "./partition_log/{}_{}_{}/"

# partitioning_method = SpectralClustering
partitioning_method_list = [
    # FMPartitioning,
    # SpectralClustering,
    # LeaderElection,
    LeaderElectionUniform
]
for partitioning_method in partitioning_method_list:
    for num_partitions in num_partitions_list:
        for topo_name in topo_name_list:
            fname = f'../../../ncflow/topologies/topology-zoo/{topo_name}.graphml'
            G = parse_and_convert_graphml.read_graph_graphml(fname)
            partition_obj = partitioning_method(num_partitions=num_partitions)
            partition_vector = partition_obj.partition(G)
            print(topo_name, partition_vector, partition_obj.name)
            path = log_dir.format(topo_name, num_partitions, partition_obj.name)
            if not os.path.isdir(path):
                os.mkdir(path)
            total_edges = len(G.edges())
            subgraph_edges = 0
            subgraph_nodes = []
            for pid in np.unique(partition_vector):
                nodes = np.argwhere(partition_vector == pid).flatten().tolist()
                subgraph_g = G.subgraph(nodes)
                subgraph_nodes.append((len(subgraph_g.nodes()), len(subgraph_g.edges())))
                subgraph_edges += len(subgraph_g.edges())
                # print(subgraph_g.edges())
                parse_and_convert_graphml.write_graph_json(subgraph_g, path + f"/cluster_{pid}.json")
            log_path = path + f"/detail.txt"
            with open(log_path, "w") as fp:
                fp.writelines(f"num total edges {total_edges} num subgraph edges {subgraph_edges} num inter-cluster edges {total_edges - subgraph_edges}\n")
                fp.writelines(f'complete graph: nodes {len(G.nodes())} edges {len(G.edges())}\n')
                for pid, (node, edge) in enumerate(subgraph_nodes):
                    fp.writelines(f"cluster {pid}: nodes {node} edges {edge}\n")
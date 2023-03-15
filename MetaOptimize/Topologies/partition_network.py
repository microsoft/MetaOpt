import imp
import itertools
import parse_and_convert_graphml
from partitioning.spectral_clustering import SpectralClustering
from partitioning.fm_partitioning import FMPartitioning
from partitioning.leader_election import LeaderElection
from partitioning.leader_election_uniform import LeaderElectionUniform
import numpy as np
import networkx as nx
from collections import defaultdict
import os

topo_name_list = [
    # ("GtsCe", 1), 
    ("Cogentco", 0),
    # ("Uninett2010", 0)
    # ("Kdl", 1)
    # ("b4-teavar", 0),
    # ("ring_200", 0),
    # ("ring_400", 0)
    ]
num_partitions_list = [
    2, 
    # 3,
    4,
    # 5,
    # 6,
    # 8, 
    # 10, 
    # 12,
    16,
    # 15, 
    20, 
    # 25,
    50,
    # 100,
]
num_shortest_paths_list = [
    2, 
    4, 
    10,
    16,
]

log_dir = "./partition_log/{}_{}_{}/"

# partitioning_method = SpectralClustering
partitioning_method_list = [
    FMPartitioning,
    SpectralClustering,
    # LeaderElection,
    # LeaderElectionUniform
]

def k_shortest_paths(G, source, target, k, weight=None):
    return list(
        itertools.islice(nx.shortest_simple_paths(G, source, target, weight=weight), k)
    )


for partitioning_method in partitioning_method_list:
    for num_partitions in num_partitions_list:
        for topo_name, is_topo_zoo in topo_name_list:
            if is_topo_zoo:
                fname = f'../../../ncflow/topologies/topology-zoo/{topo_name}.graphml'
                G = parse_and_convert_graphml.read_graph_graphml(fname)
            else:
                fname = f'{topo_name}.json'
                G = parse_and_convert_graphml.read_graph_json(fname)
            partition_obj = partitioning_method(num_partitions=num_partitions)
            partition_vector = partition_obj.partition(G, topo_name)
            print(topo_name, partition_vector, partition_obj.name)
            folder_path = log_dir.format(topo_name, num_partitions, partition_obj.name)
            if not os.path.isdir(folder_path):
                os.mkdir(folder_path)
            total_edges = len(G.edges())
            subgraph_edges = 0
            subgraph_nodes = []
            num_intra_cluster_paths_dict = defaultdict(int)
            num_total_paths_dict = defaultdict(int)
            for pid in np.unique(partition_vector):
                nodes = np.argwhere(partition_vector == pid).flatten().tolist()
                subgraph_g = G.subgraph(nodes)
                subgraph_nodes.append((len(subgraph_g.nodes()), len(subgraph_g.edges())))
                subgraph_edges += len(subgraph_g.edges())
                # print(subgraph_g.edges())
                parse_and_convert_graphml.write_graph_json(subgraph_g, folder_path + f"/cluster_{pid}.json")
            
                for num_shortest_paths in num_shortest_paths_list:
                    for (node1, node2) in itertools.combinations(subgraph_g.nodes(), 2):
                        paths = k_shortest_paths(G, node1, node2, num_shortest_paths)
                        # print(paths)
                        for s_path in paths:
                            partitions = np.unique(partition_vector[s_path])
                            if len(partitions) > 1:
                                # print(partitions)
                                num_intra_cluster_paths_dict[num_shortest_paths] += 1
                            num_total_paths_dict[num_shortest_paths] += 1

            log_path = folder_path + f"/detail.txt"
            with open(log_path, "w") as fp:
                fp.writelines(f"num total edges {total_edges} num subgraph edges {subgraph_edges} num inter-cluster edges {total_edges - subgraph_edges}\n")
                fp.writelines(f'complete graph: nodes {len(G.nodes())} edges {len(G.edges())}\n')
                for sp in num_shortest_paths_list:
                    fp.writelines(f'{sp}-shortest paths num intra cluster paths {num_intra_cluster_paths_dict[sp]}, num total paths {num_total_paths_dict[sp]} frac {num_intra_cluster_paths_dict[sp] / num_total_paths_dict[sp]}\n')
                for pid, (node, edge) in enumerate(subgraph_nodes):
                    fp.writelines(f"cluster {pid}: nodes {node} edges {edge}\n")


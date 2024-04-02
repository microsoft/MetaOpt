import imp
from itertools import permutations
import networkx as nx
import numpy as np

def to_np_arr(arr):
    return arr if isinstance(arr, np.ndarray) else np.array(arr)


def is_partition_valid(G, nodes_in_part):
    G_sub = G.subgraph(nodes_in_part)
    for src, target in permutations(G_sub.nodes, 2):
        if not nx.has_path(G_sub, src, target):
            print(src, target)
            return False
    return True


def all_partitions_contiguous(prob, p_v):

    partition_vector = to_np_arr(p_v)
    for k in np.unique(partition_vector):
        if not is_partition_valid(
                prob,
                prob.G.subgraph(
                    np.argwhere(partition_vector == k).flatten())):
            print(k)
            return False
    return True
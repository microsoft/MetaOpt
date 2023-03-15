import numpy as np
import networkx as nx
from collections import defaultdict
import os
import parse_and_convert_graphml
import itertools
import json


fname = f'Cogentco.json'
G = parse_and_convert_graphml.read_graph_json(fname)
# print(G)
print(nx.diameter(G))
# demandFile = f"../logs/realistic_constraints/Cogentco_10_DemandPinning_0.5_0.05_4_2023_1_13_11_27_15_374/" + \
#              f"primal_dual_DemandPinning_density_1_maxLargeDistance_1_maxSmallDistance-1_LargeDemandLB_0.25/demands.txt"

log_dir = "../logs/realistic_constraints/"
demand_file_list = [
    "Cogentco_10_DemandPinning_0.5_0.05_4_2023_1_31_20_4_7_686/primal_dual_DemandPinning_density_1_maxLargeDistance_2_maxSmallDistance-1_LargeDemandLB_0.25/demands.txt",
    "Cogentco_10_DemandPinning_0.5_0.05_4_2023_2_1_12_21_16_516/primal_dual_DemandPinning_density_1_maxLargeDistance_-1_maxSmallDistance-1_LargeDemandLB_0.25/demands.txt",
    "Cogentco_10_DemandPinning_0.5_0.05_4_2023_2_1_12_21_16_516/primal_dual_DemandPinning_density_1_maxLargeDistance_4_maxSmallDistance-1_LargeDemandLB_0.25/demands.txt",
]

for demand_file in demand_file_list:
    print(f"=============== {demand_file} ===============")
    with open(log_dir + demand_file, "r") as fp:
        demands = dict(json.load(fp))
    print(len(demands))
    num_nodes = len(G.nodes())
    num_pairs = num_nodes * (num_nodes - 1)
    print("num nodes: ", num_nodes)
    print("num pairs: ", num_pairs)
    num_positive_demands = 0

    cdf_dict = defaultdict(int)
    for pair, rate in demands.items():
        src, dst = pair.split(", ")
        src = int(src[1:])
        dst = int(dst[:-1])
        if rate > 0.00001:
            num_positive_demands += 1
        if rate > 0.25:
            path_len = nx.shortest_path_length(G, src, dst)
            # assert path_len == 1
            cdf_dict[path_len] += 1
        
    print("num non zeros:", num_positive_demands)
    print("density:", num_positive_demands / num_pairs)
    print("cdf large flows:", cdf_dict)

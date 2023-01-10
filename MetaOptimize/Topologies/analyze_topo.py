import numpy as np
import networkx as nx
from collections import defaultdict
import os
import parse_and_convert_graphml
import itertools


fname = f'Cogentco.json'
G = parse_and_convert_graphml.read_graph_json(fname)
print(G)
link_to_num_flows = defaultdict(int)
all_pair_sp = dict(nx.all_pairs_shortest_path(G))
# print(all_pair_sp)
for (n1, n2) in itertools.permutations(G.nodes(), 2):
    sp = all_pair_sp[n1][n2]
    for (e1, e2) in zip(sp, sp[1:]):
        link_to_num_flows[e1, e2] += 1

print(link_to_num_flows)
    
import json
import numpy as np
from networks import ring_topology
import networkx as nx
from networkx.readwrite import json_graph

def read_graph_graphml(fname):
    assert fname.endswith('.graphml')
    file_G = nx.read_graphml(fname).to_directed()
    if isinstance(file_G, nx.MultiDiGraph):
        file_G = nx.DiGraph(file_G)
    G = []
    for scc_ids in nx.strongly_connected_components(file_G):
        scc = file_G.subgraph(scc_ids)
        if len(scc) > len(G):
            print("len is: " + str(len(scc)))
            G = scc
    G = nx.convert_node_labels_to_integers(G)
    for u,v in G.edges():
        G[u][v]['capacity'] = 1000.0
    return G

def write_graph_json(G: nx.Graph, fname):
    assert fname.endswith('json')
    with open(fname, 'w') as w:
        json.dump(json_graph.node_link_data(G), w)


def read_graph_json(fname):
    assert fname.endswith('json')
    with open(fname, "r") as fp:
        node_link_data = json.load(fp)
    G = json_graph.node_link_graph(node_link_data)
    return G



# # # print("Hi")
# topo_name_list = [
#     # "GtsCe", 
#     # "Cogentco",
#     # "Kdl",
#     "Uninett2010",
# ]
# for topo_name in topo_name_list:
#     fname = f'../../../ncflow/topologies/topology-zoo/{topo_name}.graphml'
#     G = read_graph_graphml(fname)
#     fname = f'./{topo_name}.json'
#     write_graph_json(G, fname)

# num_nodes_list = [
#     20, 
#     # 200, 
#     # 400,
# ]
# for num_nodes in num_nodes_list:
#     fname = f"./ring_{num_nodes}.json"
#     G = ring_topology(num_nodes=num_nodes, cap=1000)
#     # for edge in G.edges:
#     #     print(edge)
#     write_graph_json(G, fname=fname)


# G = read_graph_json("../Topologies/b4-teavar.json")

# print(nx.diameter(G))

import json
import numpy as np
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

def write_graph_json(G, fname):
    assert fname.endswith('json')
    with open(fname, 'w') as w:
        json.dump(json_graph.node_link_data(G), w)



print("Hi")
fname = '/mnt/d/Pooria/risk-aware-automitigation/traffic_engineering/ncflow/topologies/topology-zoo/Ion.graphml'
G = read_graph_graphml(fname)
fname = '/mnt/d/Pooria/risk-aware-automitigation/traffic_engineering/ncflow/topologies/topology-zoo/Ion.json'
write_graph_json(G, fname)


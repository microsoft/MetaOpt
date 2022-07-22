import numpy as np


class AbstractPartitioningMethod(object):

    def __init__(self, *, num_partitions=None, weighted=True):
        if isinstance(num_partitions, int):
            self._num_partitions = num_partitions

        self._use_cache = True
        self._weighted = weighted

        self._best_partitions = {
        }

    @property
    def name(self):
        raise Exception("not implemented")
        
    @property
    def use_cache(self):
        return self._use_cache

    @use_cache.setter
    def use_cache(self, use_cache):
        self._use_cache = use_cache

    @property
    def G(self):
        return self._G

    @property
    def partition_vector(self):
        return self._partition_vector

    @property
    def size_of_largest_partition(self):
        counts = np.bincount(self._partition_vector)
        return counts[np.argmax(counts)]

    @property
    def largest_partition_index(self):
        counts = np.bincount(self._partition_vector)
        return np.argmax(counts)

    @property
    def num_partitions(self):
        if not hasattr(self, '_num_partitions'):
            return -1
        return self._num_partitions

    @property
    def weighted(self):
        return self._weighted

    # Private method #
    def _default_num_partitions(self, G):
        return int(np.sqrt(len(G.nodes)))

    def partition(self, G, override_cache=False):
        if not override_cache and self._use_cache and G.name in self._best_partitions:
            return self._best_partitions[G.name]

        self._partition_vector = self._partition_impl(G)
        return self._partition_vector

    #################
    # Public method #
    #################
    @property
    def name(self):
        raise NotImplementedError(
            'name needs to be implemented in the subclass: {}'.format(
                self.__class__))

    def _partition_impl(self, G):
        raise NotImplementedError(
            '_partition_impl needs to be implemented in the subclass: {}'.format(
                self.__class__))
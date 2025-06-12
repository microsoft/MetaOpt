# Failure Analysis Module

This module provides tools for analyzing and optimizing network topologies under failure scenarios. It includes various encoders and generators for capacity augmentation, failure analysis, and optimization.

## Core Components

### Solution Classes
- `CapacityAugmentSolution`: Represents the solution for capacity augmentation problems, containing the status of links that need to be augmented.
- `FailureAnalysisOptimizationSolution`: Represents the solution for failure analysis problems, including demands, link status, flow allocations, and optimization objectives.

### Capacity Augmentation
- `CapacityAugmentEncoder`: Base class for encoding capacity augmentation problems. It finds the minimum number of links to add to carry target demand after failures.
- `CapacityAugmentsOnExisting`: Specialized encoder that only increases capacity on existing links rather than adding new ones.
- `CapacityAugmenterV2`: Enhanced version of the capacity augmentation encoder with additional features.

### Failure Analysis
- `FailureAnalysisEncoder`: Base class for encoding failure analysis problems.
- `FailureAnalysisEncoderWithUnequalPaths`: Extends the base encoder to handle unequal path scenarios.
- `FailureAnalysisMLUCutEncoder`: Specialized encoder for Maximum Link Utilization (MLU) cut scenarios.

### Adversarial Generators
- `FailureAnalysisAdversarialGenerator`: Main generator for creating adversarial scenarios in failure analysis.
- `FailureAnalysisAdversarialGeneratorForUnequalPaths`: Specialized generator for handling unequal path scenarios.
- `FailureAnalysisWithMetaNodeAdversarialGenerator`: Generator that supports meta-node scenarios in failure analysis.

## Key Features

1. **Capacity Augmentation**
   - Find minimum links to add for target demand
   - Increase capacity on existing links
   - Handle various network topologies

2. **Failure Analysis**
   - Analyze network behavior under failures
   - Comprehensive analysis of the impact across **all** possible failure scenarios.
   - Support for meta-nodes and unequal paths; specific failure scenario modeling which includes only investigating failures that do not disconnect the graph.

3. **TE Objectives**
   - Maximize total allocated flow
   - Maximum Link Utilization (MLU)

## Example usage:

- Please see MetaOptimize.Test for example test cases that use different parts of this code.

## Notes
- All classes are generic and can work with different variable types (TVar) and solution types (TSolution)
- The module supports both binary and continuous optimization problems
- Various path computation methods are supported (KSP, etc.) 
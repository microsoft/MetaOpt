# Introduction 
This codebase contains the source code for the MetaOpt project described in the paper:
Finding Adversarial Inputs for Heuristics using Multi-level Optimization from NSDI 2024.

You can find the project webpage here:
https://www.microsoft.com/en-us/research/project/finding-adversarial-inputs-for-heuristics/publications/

# How to try MetaOpt

We provide multiple example Main functions that you can try in MetaOptimize.Cli/program.cs for
different heuristics in vector bin packing, packet scheduling, and traffic engineering.

We also provide many unit-tests that can serve as a starting point in: MetaOptimize.Test

You can use MetaOpt with either the Gurobi optimization solver (https://www.gurobi.com/documentation/current/examples/cs_examples.html) 
or with Zen (https://dl.acm.org/doi/10.1145/3422604.3425930).
Make sure you install both solvers and to configure the proper Gurobi license.

# How to use MetaOpt to analyze my heuristics

If you would like to analyze a heuristic that we have not modeled as part of MetaOpt yet, here are the steps to follow:

1- Model your heuristic as either a convex optimization problem or a feasibility problem which our solvers can support.
How you model the problem is important and can significantly influence MetaOpt's ability to scale (and also whether Gurobi can run into
numerical issues). If you run into trouble with this step, feel free to contact the MetaOpt authors at: namyar@usc.edu and bearzani@microsoft.com.

2- You need to write an "Encoder" library for both the heuristic you want to analyze and the optimal form of the problem (or if you want to compare two heuristics, for the other heuristics).
We strongly recommend looking at the Traffic Engineering, VBP, and packet scheduling Encoders we have provided as part of the MetaOpt code-base for examples. 
Encoders must meet the specifications in the IEncoder.cs library. 
If your problem is a convex optimization problem (and NOT a feasibility problem), then, you also need to pick the type of re-write you want to use (KKT or quantized primal dual).
We recommend the quantized primal dual approach to achieve better scalability.

To use the quantized primal dual approach you also need to specify the quantization levels.

3- Once you have both of the encoders in place, you have to write an adversarial input generator. See the TE, and VBP folders for examples.
To solve the MetaOpt problem, you need to create the "input variables" in the adversarial input generator and pass the SAME input variables to the encoders
for the algorithms you want to analyze.
Also notice that the adversarial input generator and the two encoders need to use the same solver instance.

To see examples of this workflow, we highly recommend looking at the test cases in MetaOptimize.Test.

# Who do I contact if I have questions

Behnaz Arzani (bearzani@microsoft.com)
Pooria Namyar (namyar@usc.edu)
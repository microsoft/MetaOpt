# Modeling and Explaining Heuristics with Flow Networks

This repo is written in C# using .NET, so you'll need to [install it](https://dot.net).

You'll also need a license for the [Gurobi solver](https://gurobi.com).
If you're an academic, follow the instructions on the Gurobi website. If not, ask your organization how to use its license.

You can develop in anything you want on any OS, such as Visual Studio on Windows or Visual Studio Code on Windows, macOS, and Linux.
Either run the code from an IDE, or use the `dotnet` command-line tool built into .NET.

## Usage

See [Program.cs](./Flows.Cli/Program.cs) for an end-to-end example.

The flow network API is documented [here](./Flows/Network/API.cs).
Anything you write with that API can be compiled, but it can still be slow or impossible if you write constraints that are intrinsically complex or unsatisfiable.

If you want to model a new problem, see [bin packing](./Flows.Cli/BinPacking) and [traffic engineering](./Flows.Cli/TrafficEngineering) for examples.
You may need to design new node behaviors, which involves one function for the node's objective if needed, and one function for the node's constraints.
You can see node behavior examples in [NodeBehaviors.cs](./Flows.Cli/NodeBehaviors.cs).

If you want to model a heuristic, see the problem examples for heuristic examples.
You need to provide two functions: one that sorts the nodes, and one that assigns flows to arcs for each node.

## Architecture

The [Flows](./Flows) project is composed of the following modules in a cleanly layered stack, from bottom to top:
- `Collections`: immutable collections that preserve insertion order, to ensure determinism.
- `Solving`: low-level optimization solving concepts such as expressions, constraints, and solvers.
- `Network`: high-level flow network modeling concepts.
- `Explainability`: hypotheses and their testing.
- Top-level: bringing it all together to model problems and heuristics.

There are no "built-in" heuristics or node behaviors, everything is defined in the `Cli` project that demonstrates how to use the system.
This ensures users can do everything we, the system designers, can do.

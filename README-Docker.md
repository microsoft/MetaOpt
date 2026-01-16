# MetaOptimize Docker Deployment

Complete guide for containerizing MetaOptimize, a CLI tool for adversarial optimization across four problem domains: Traffic Engineering, Bin Packing, PIFO packet scheduling, and Failure Analysis.

**Note:** Docker is for local development and testing. MSRHub users should use `dotnet run` directly on the hub.

## Quick Start

### Using Docker

```bash
# Build the image
docker build -t metaoptimize:latest .

# Show help
docker run --rm metaoptimize:latest --help

# Run with free OR-Tools solver (default)
docker run --rm metaoptimize:latest --problemType BinPacking
```

### Using Docker Compose (Recommended for Local Dev)

```bash
# Build
docker compose build

# Show help
docker compose run --rm metaopt --help

# List all available profiles
docker compose config --services

# Run specific profiles
docker compose run --rm te-pop
docker compose run --rm binpacking
docker compose run --rm pifo
docker compose run --rm failure
```

---

## Solver Options

MetaOptimize supports two solver backends:

| Solver            | License Required | Speed  | Use Case                                 |
| ----------------- | ---------------- | ------ | ---------------------------------------- |
| **OR-Tools, Zen** | ❌ Free           | Medium | Default, suitable for most problems      |
| **Gurobi**        | ✅ Commercial     | Fast   | Large-scale optimization, enterprise use |

**Default:** OR-Tools - works out of the box, no configuration needed.

---

## Problem Types

MetaOptimize supports four problem types, each finding adversarial inputs that maximize performance gaps between optimal and heuristic algorithms.

### Traffic Engineering

Finds worst-case demand patterns that maximize the gap between optimal routing and heuristic algorithms.

**One-Liner (OR-Tools / default solver):**

```bash
docker run --rm -v "$(pwd)/Topologies:/app/Topologies:ro" metaoptimize:latest --problemType TrafficEngineering --topologyFile /app/Topologies/simple.json --heuristic Pop --paths 2 --verbose
```

**One-Liner (Gurobi + license file mounted):**

```bash
docker run --rm -v "$(pwd)/Topologies:/app/Topologies:ro" -v "$(pwd)/licenses:/app/licenses:ro" -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic metaoptimize:latest --solver Gurobi --problemType TrafficEngineering --topologyFile /app/Topologies/simple.json --heuristic Pop --paths 2 --verbose
```

**Key Options:**

| Parameter         | Description                                                                                       | Default                       |
| ----------------- | ------------------------------------------------------------------------------------------------- | ----------------------------- |
| `--topologyFile`  | Topology JSON file path                                                                           | `/app/Topologies/simple.json` |
| `--heuristic`     | Heuristic: `Pop`, `DemandPinning`, `ExpectedPop`, `PopDp`, `ModifiedDp`                           | `Pop`                         |
| `--solver`        | Solver: `OrTools` (free) or `Gurobi` (requires license)                                           | `OrTools`                     |
| `--paths`         | Maximum paths per source-dest pair                                                                | `2`                           |
| `--slices`        | Number of Pop partitions                                                                          | `2`                           |
| `--pinthreshold`  | Demand pinning threshold                                                                          | `0.5`                         |
| `--method`        | Gap-finding method: `Direct`, `Search`, `FindFeas`, `Random`, `HillClimber`, `SimulatedAnnealing` | `Direct`                      |
| `--innerencoding` | Inner encoding: `KKT` or `PrimalDual`                                                             | `KKT`                         |
| `--demandlist`    | Comma-separated demand levels (for PrimalDual), no spaces                                         | `0`                           |
| `--timeout`       | Solver timeout in seconds                                                                         | `1000.0`                      |
| `--gurobithreads` | Gurobi thread count (0=auto, 1=deterministic)                                                     | `1`                           |
| `--verbose`       | Enable verbose output (flag only, no value)                                                       | -                             |

---

### Bin Packing

Finds item sizes that maximize the gap between optimal bin packing and First-Fit heuristics.

**Docker Compose Profiles:**

```bash
docker compose run --rm binpacking      # Default: 1D
docker compose run --rm binpacking-2d   # 2D bin packing
docker compose run --rm binpacking-3d   # 3D bin packing
```

**One-Liner:**

```bash
docker run --rm -v "${PWD}\Topologies:/app/Topologies:ro" -v "${PWD}\licenses:/app/licenses:ro" -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic metaoptimize:latest --problemType BinPacking --solver Gurobi --numBins 6 --numDemands 9 --numDimensions 2 --optimalBins 3 --ffMethod FFDSum --verbose
```

**Key Options:**

| Parameter         | Description                                                  | Default           |
| ----------------- | ------------------------------------------------------------ | ----------------- |
| `--solver`        | Solver: `OrTools` (free) or `Gurobi` (requires license)      | `OrTools`         |
| `--numBins`       | Number of bins available                                     | `6`               |
| `--numDemands`    | Number of items to pack                                      | `9`               |
| `--numDimensions` | Number of dimensions (e.g., 2D = weight + volume)            | `2`               |
| `--binCapacity`   | Comma-separated capacities per dimension                     | `1.00001,1.00001` |
| `--optimalBins`   | Target optimal bin count                                     | `3`               |
| `--ffMethod`      | First-Fit method: `FF`, `FFDSum`, `FFDProd`, `FFDDiv`        | `FFDSum`          |
| `--breakSymmetry` | Enable symmetry breaking: `true` or `false` (value required) | `false`           |
| `--timeout`       | Solver timeout in seconds                                    | `1000.0`          |
| `--verbose`       | Enable verbose output (flag only, no value)                  | -                 |

---

### PIFO (Packet Scheduling)

Finds packet arrival patterns that maximize scheduling inversions between SP-PIFO and AIFO algorithms.

**Docker Compose Profile:**

```bash
docker compose run --rm pifo
```

**One-Liner:**

```bash
docker run --rm metaoptimize:latest --problemType PIFO  --numPackets 18 --maxRank 8 --numQueues 4 --maxQueueSize 12 --windowSize 12 --burstParam 0.1 --timeout 600 --verbose
```

---

### Failure Analysis

Finds worst-case link failure scenarios that maximize network throughput degradation.

**Docker Compose Profile:**

```bash
docker compose run --rm failure
```

**One-Liner:**

```bash
docker run --rm metaoptimize:latest --problemType FailureAnalysis --useDefaultTopology true --maxNumFailures 1 --failureProbThreshold 0.25 --verbose
```

---

## Common Options

These options apply to all problem types:

| Parameter         | Description                                                                 | Default              |
| ----------------- | --------------------------------------------------------------------------- | -------------------- |
| `--problemType`   | Problem type: `TrafficEngineering`, `BinPacking`, `PIFO`, `FailureAnalysis` | `TrafficEngineering` |
| `--solver`        | Solver backend: `OrTools` (free) or `Gurobi` (requires license)             | `OrTools`            |
| `--timeout`       | Solver timeout in seconds                                                   | `1000.0`             |
| `--gurobithreads` | Gurobi thread count (0=auto, 1=single-threaded for determinism)             | `1`                  |
| `--seed`          | Random seed for reproducibility                                             | `1`                  |
| `--verbose`       | Enable verbose output with detailed logs (flag only, no value)              | -                    |
| `--debug`         | Enable debug messages (flag only, no value)                                 | -                    |

---

## Docker Setup

### Directory Structure

```
MetaOpt/
├── Dockerfile
├── .dockerignore
├── docker-compose.yml
├── Topologies/
│   ├── simple.json
│   └── Swan.json
├── licenses/
│   └── gurobi.lic
└── output/                  # Container writes results here (if you mount it)
```

### Volume Mounts

| Container Path    | Purpose                                 | Recommended Host Mount            |
| ----------------- | --------------------------------------- | --------------------------------- |
| `/app/Topologies` | Network topology JSON files (read-only) | `./Topologies:/app/Topologies:ro` |
| `/app/licenses`   | Gurobi license files (read-only)        | `./licenses:/app/licenses:ro`     |
| `/app/output`     | Output files from optimization runs     | `./output:/app/output`            |

---

## Gurobi License Configuration (Optional)

MetaOptimize defaults to free solvers. Use Gurobi only if you have a license.

### License File (Recommended for Docker)

Place `gurobi.lic` under `./licenses/gurobi.lic` on the host, mount it into the container, and set `GRB_LICENSE_FILE` to the **in-container** path:

✅ **Linux/macOS bash**

```bash
docker run --rm -v "$(pwd)/Topologies:/app/Topologies:ro" -v "$(pwd)/licenses:/app/licenses:ro" -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic metaoptimize:latest --solver Gurobi --problemType TrafficEngineering --topologyFile /app/Topologies/simple.json --heuristic Pop --paths 2 --verbose
```

✅ **Windows PowerShell**

```powershell
docker run --rm -v "${PWD}\Topologies:/app/Topologies:ro" -v "${PWD}\licenses:/app/licenses:ro" -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic metaoptimize:latest --solver Gurobi --problemType TrafficEngineering --topologyFile /app/Topologies/simple.json --heuristic Pop --paths 2 --verbose
```

✅ **Windows Git Bash (MSYS2)**

```bash
MSYS_NO_PATHCONV=1 docker run --rm -v "$(pwd -W)/Topologies:/app/Topologies:ro" -v "$(pwd -W)/licenses:/app/licenses:ro" -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic metaoptimize:latest --solver Gurobi --problemType TrafficEngineering --topologyFile /app/Topologies/simple.json --heuristic Pop --paths 2 --verbose
```

**Important notes:**

* `GRB_LICENSE_FILE` must be a normal absolute Linux path inside the container, like:

  * ✅ `/app/licenses/gurobi.lic`
  * ❌ `//app/licenses/gurobi.lic` (double slash can fail)
* On **Git Bash**, avoid `$(pwd)` for `-v ...` mounts. Use `$(pwd -W)` and `MSYS_NO_PATHCONV=1` to prevent path mangling (this is what caused paths like `C:/Program Files/Git/...` and folders like `Topologies;C`).

### Quick “sanity check” inside the container

MetaOptimize’s image entrypoint runs the CLI, so to run `ls` you must override the entrypoint:

✅ **PowerShell**

```powershell
docker run --rm --entrypoint sh -v "${PWD}\licenses:/app/licenses:ro" metaoptimize:latest -c "ls -la /app/licenses"
```

✅ **Git Bash**

```bash
MSYS_NO_PATHCONV=1 docker run --rm --entrypoint sh -v "$(pwd -W)/licenses:/app/licenses:ro" metaoptimize:latest -c "ls -la /app/licenses"
```

(If you don’t override the entrypoint, `ls -la ...` gets passed to the MetaOptimize CLI and you’ll see CLI option parsing errors like `Option 'l, lambda' is defined with a bad format.`)

### Token Server (Alternative)

If your environment uses a token server:

```bash
docker run --rm -e GRB_TOKEN_SERVER="your-host:port" metaoptimize:latest --problemType BinPacking --solver Gurobi --verbose
```

### Web License Service (Alternative)

```bash
docker run --rm -e GRB_WLSACCESSID="your-access-id" -e GRB_WLSSECRET="your-secret" -e GRB_LICENSEID="your-license-id" metaoptimize:latest --problemType BinPacking --solver Gurobi --verbose
```

---

## Troubleshooting

### Git Bash path / license issues (Windows)

**Symptom 1:** Gurobi tries to open something like:
`C:/Program Files/Git/app/licenses/gurobi.lic`

**Fix (Git Bash):** Use:

* `MSYS_NO_PATHCONV=1`
* `$(pwd -W)` for all `-v` mounts
* `GRB_LICENSE_FILE=/app/licenses/gurobi.lic` (single slash)

**Symptom 2:** A weird folder appears on the host like `Topologies;C`

**Cause:** MSYS path conversion + Docker `-v host:container:mode` parsing collided.

**Fix:** Same as above (disable conversion and use Windows path via `pwd -W`).

### Gurobi license file not found

If you see:

```
ERROR: Unable to open Gurobi license file '/app/licenses/gurobi.lic'
```

Run the sanity check command:

```bash
docker run --rm --entrypoint sh -v "$(pwd)/licenses:/app/licenses:ro" metaoptimize:latest -c "ls -la /app/licenses"
```

…and confirm the file is visible.

---

## MSRHub Usage

MSRHub users run MetaOptimize directly without Docker:

```bash
dotnet run --project MetaOptimize.Cli -- --problemType BinPacking --verbose
```

The Gurobi license is configured on the MSRHub server by administrators.

---

## Getting Help

```bash
docker run --rm metaoptimize:latest --help
docker run --rm metaoptimize:latest --version
```

---

## Technical Details

| Property                 | Value                                  |
| ------------------------ | -------------------------------------- |
| **Base Image (build)**   | `mcr.microsoft.com/dotnet/sdk:8.0`     |
| **Base Image (runtime)** | `mcr.microsoft.com/dotnet/runtime:8.0` |
| **Gurobi Version**       | 11.0.3 (optional)                      |
| **.NET Version**         | 8.0                                    |
| **Default Solver**       | OR-Tools or Zen - free                 |
| **Working Directory**    | `/app`                                 |
| **Entry Point**          | `dotnet MetaOptimize.Cli.dll`          |

---

If you want, paste your repo path conventions (`Topologies` vs `topologies`, `Swan.json` vs `swan.json`) and I’ll adjust the examples to match your actual filenames exactly—but the key fix (Git Bash: `MSYS_NO_PATHCONV=1` + `pwd -W` + single-slash `/app/...`) should stay as written.

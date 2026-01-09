# MetaOptimize Docker Deployment

Complete guide for containerizing and deploying MetaOptimize, a CLI tool for adversarial optimization across four problem domains: Traffic Engineering, Bin Packing, PIFO packet scheduling, and Failure Analysis.

## Quick Start

### Using Docker

**Linux/Mac:**
```bash
# Build the image
docker build -t metaoptimize:latest .

# Show help
docker run --rm metaoptimize:latest --help

# Run with free OR-Tools solver (default)
docker run --rm metaoptimize:latest --problemType BinPacking
```

**Windows PowerShell:**
```powershell
# Build the image
docker build -t metaoptimize:latest .

# Show help
docker run --rm metaoptimize:latest --help

# Run with free OR-Tools solver (default)
docker run --rm metaoptimize:latest --problemType BinPacking
```

### Using Docker Compose (Recommended)

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

| Solver | License Required | Speed | Use Case |
|--------|------------------|-------|----------|
| **Zen (OR-Tools)** | ❌ Free | Medium | Default, suitable for most problems |
| **Gurobi** | ✅ Commercial | Fast | Large-scale optimization, enterprise use |

**Default:** Zen (OR-Tools) - works out of the box, no configuration needed.

---

## Problem Types

MetaOptimize supports four problem types, each finding adversarial inputs that maximize performance gaps between optimal and heuristic algorithms.

### Traffic Engineering

Finds worst-case demand patterns that maximize the gap between optimal routing and heuristic algorithms.

**Docker Compose Profiles:**
```bash
docker compose run --rm te-pop              # Pop heuristic with simple topology
docker compose run --rm te-demandpinning    # DemandPinning heuristic
docker compose run --rm te-primaldual       # PrimalDual encoding
docker compose run --rm te-swan             # Swan topology
```

**Custom Run - Linux/Mac:**
```bash
docker run --rm \
  -v "$(pwd)/Topologies:/app/Topologies:ro" \
  metaoptimize:latest \
  --problemType TrafficEngineering \
  --topologyFile Topologies/simple.json \
  --heuristic Pop \
  --paths 2 \
  --verbose
```

**Custom Run - Windows PowerShell:**
```powershell
docker run --rm `
  -v "$PWD/Topologies:/app/Topologies:ro" `
  metaoptimize:latest `
  --problemType TrafficEngineering `
  --topologyFile Topologies/simple.json `
  --heuristic Pop `
  --paths 2 `
  --verbose
```

**Key Options:**

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--topologyFile` | Topology JSON file path (relative to /app) | `Topologies/simple.json` |
| `--heuristic` | Heuristic: `Pop`, `DemandPinning`, `ExpectedPop`, `PopDp`, `ModifiedDp` | `Pop` |
| `--solver` | Solver: `Zen` (free) or `Gurobi` (requires license) | `Zen` |
| `--paths` | Maximum paths per source-dest pair | `2` |
| `--slices` | Number of Pop partitions | `2` |
| `--pinthreshold` | Demand pinning threshold | `0.5` |
| `--method` | Gap-finding method: `Direct`, `Search`, `FindFeas`, `Random`, `HillClimber`, `SimulatedAnnealing` | `Direct` |
| `--innerencoding` | Inner encoding: `KKT` or `PrimalDual` | `KKT` |
| `--demandlist` | Comma-separated demand levels (for PrimalDual), no spaces | `0` |
| `--timeout` | Solver timeout in seconds | `1000.0` |
| `--gurobithreads` | Gurobi thread count (0=auto, 1=deterministic) | `1` |
| `--verbose` | Enable verbose output (flag only, no value) | - |

---

### Bin Packing

Finds item sizes that maximize the gap between optimal bin packing and First-Fit heuristics.

**Docker Compose Profiles:**
```bash
docker compose run --rm binpacking         # Default: FFDSum, 6 bins, 9 items
docker compose run --rm binpacking-ff      # First-Fit without sorting
docker compose run --rm binpacking-large   # Larger problem: 10 bins, 15 items
```

**Custom Run - Linux/Mac:**
```bash
docker run --rm \
  metaoptimize:latest \
  --problemType BinPacking \
  --numBins 6 \
  --numDemands 9 \
  --numDimensions 2 \
  --optimalBins 3 \
  --ffMethod FFDSum \
  --verbose
```

**Custom Run - Windows PowerShell:**
```powershell
docker run --rm `
  metaoptimize:latest `
  --problemType BinPacking `
  --numBins 6 `
  --numDemands 9 `
  --numDimensions 2 `
  --optimalBins 3 `
  --ffMethod FFDSum `
  --verbose
```

**Key Options:**

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--solver` | Solver: `Zen` (free) or `Gurobi` (requires license) | `Zen` |
| `--numBins` | Number of bins available | `6` |
| `--numDemands` | Number of items to pack | `9` |
| `--numDimensions` | Number of dimensions (e.g., 2D = weight + volume) | `2` |
| `--binCapacity` | Comma-separated capacities per dimension | `1.00001,1.00001` |
| `--optimalBins` | Target optimal bin count | `3` |
| `--ffMethod` | First-Fit method: `FF`, `FFDSum`, `FFDProd`, `FFDDiv` | `FFDSum` |
| `--breakSymmetry` | Enable symmetry breaking: `true` or `false` (value required) | `false` |
| `--timeout` | Solver timeout in seconds | `1000.0` |
| `--verbose` | Enable verbose output (flag only, no value) | - |

---

### PIFO (Packet Scheduling)

Finds packet arrival patterns that maximize scheduling inversions between SP-PIFO and AIFO algorithms.

**Docker Compose Profiles:**
```bash
docker compose run --rm pifo        # Default: 18 packets, 8 ranks, 4 queues
docker compose run --rm pifo-large  # Larger: 24 packets, 10 ranks, 6 queues
```

**Custom Run - Linux/Mac:**
```bash
docker run --rm \
  metaoptimize:latest \
  --problemType PIFO \
  --numPackets 18 \
  --maxRank 8 \
  --numQueues 4 \
  --maxQueueSize 12 \
  --verbose
```

**Custom Run - Windows PowerShell:**
```powershell
docker run --rm `
  metaoptimize:latest `
  --problemType PIFO `
  --numPackets 18 `
  --maxRank 8 `
  --numQueues 4 `
  --maxQueueSize 12 `
  --verbose
```

**Key Options:**

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--solver` | Solver: `Zen` (free) or `Gurobi` (requires license) | `Zen` |
| `--numPackets` | Number of packets | `18` |
| `--maxRank` | Maximum rank value | `8` |
| `--numQueues` | Number of queues for SP-PIFO | `4` |
| `--maxQueueSize` | Maximum queue size | `12` |
| `--windowSize` | AIFO window size | `12` |
| `--burstParam` | AIFO burst parameter | `0.1` |
| `--timeout` | Solver timeout in seconds | `1000.0` |
| `--verbose` | Enable verbose output (flag only, no value) | - |

---

### Failure Analysis

Finds worst-case link failure scenarios that maximize network throughput degradation.

**Docker Compose Profiles:**
```bash
docker compose run --rm failure        # Single link failure
docker compose run --rm failure-multi  # Multiple simultaneous failures
```

**Custom Run - Linux/Mac:**
```bash
docker run --rm \
  metaoptimize:latest \
  --problemType FailureAnalysis \
  --useDefaultTopology true \
  --maxNumFailures 1 \
  --failureProbThreshold 0.25 \
  --verbose
```

**Custom Run - Windows PowerShell:**
```powershell
docker run --rm `
  metaoptimize:latest `
  --problemType FailureAnalysis `
  --useDefaultTopology true `
  --maxNumFailures 1 `
  --failureProbThreshold 0.25 `
  --verbose
```

**Key Options:**

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--solver` | Solver: `Zen` (free) or `Gurobi` (requires license) | `Zen` |
| `--useDefaultTopology` | Use built-in 4-node diamond topology: `true` or `false` (value required) | `true` |
| `--topologyFile` | Custom topology file (if `useDefaultTopology` is `false`) | `Topologies/simple.json` |
| `--maxNumFailures` | Maximum simultaneous link failures | `1` |
| `--numExtraPaths` | Extra paths for failure rerouting | `1` |
| `--demandlist` | Comma-separated demand quantization levels | `0` |
| `--failureProbThreshold` | Minimum link failure probability to consider | `0.25` |
| `--scenarioProbThreshold` | Minimum failure scenario probability | `0.0` |
| `--innerencoding` | Inner encoding: `KKT` or `PrimalDual` | `KKT` |
| `--timeout` | Solver timeout in seconds | `1000.0` |
| `--verbose` | Enable verbose output (flag only, no value) | - |

---

## Common Options

These options apply to all problem types:

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--problemType` | Problem type: `TrafficEngineering`, `BinPacking`, `PIFO`, `FailureAnalysis` | `TrafficEngineering` |
| `--solver` | Solver backend: `Zen` (free) or `Gurobi` (requires license) | `Zen` |
| `--timeout` | Solver timeout in seconds | `1000.0` |
| `--gurobithreads` | Gurobi thread count (0=auto, 1=single-threaded for determinism) | `1` |
| `--seed` | Random seed for reproducibility | `1` |
| `--verbose` | Enable verbose output with detailed logs (flag only, no value) | - |
| `--debug` | Enable debug messages (flag only, no value) | - |

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
└── output/                  # Container writes results here
```

### Volume Mounts

| Container Path | Purpose | Recommended Host Mount |
|---------------|---------|------------------------|
| `/app/Topologies` | Network topology JSON files (read-only) | `./Topologies:/app/Topologies:ro` |
| `/app/output` | Output files from optimization runs | `./output:/app/output` |

---

## Gurobi License Configuration (Optional)

**Default:** MetaOptimize uses the free OR-Tools solver (Zen). Gurobi is optional for users who need faster solving or have existing licenses.

### For MSRHub Deployment (Recommended)

**MSRHub administrators** set environment variables on the MSRHub server:

```bash
# On MSRHub server (requires admin/SSH access)
export GRB_TOKEN_SERVER="10.137.58.158:41954"

# Make it persistent
echo 'export GRB_TOKEN_SERVER="10.137.58.158:41954"' | sudo tee -a /etc/environment
```

Docker containers automatically inherit these environment variables. No additional configuration needed in deployment files.

**Test with Gurobi:**
```bash
docker run metaoptimize:latest --problemType BinPacking --solver Gurobi --verbose
```

---

### For Local Development

#### Option 1: Environment Variable (Recommended)

**Linux/Mac:**
```bash
# Set for current session
export GRB_TOKEN_SERVER="your-server:port"

# Run with Gurobi
docker run --rm \
  -e GRB_TOKEN_SERVER \
  metaoptimize:latest \
  --problemType BinPacking \
  --solver Gurobi
```

**Windows PowerShell:**
```powershell
# Set for current session
$env:GRB_TOKEN_SERVER="your-server:port"

# Run with Gurobi
docker run --rm `
  -e GRB_TOKEN_SERVER=$env:GRB_TOKEN_SERVER `
  metaoptimize:latest `
  --problemType BinPacking `
  --solver Gurobi
```

---

#### Option 2: License File

**Linux/Mac:**
```bash
docker run --rm \
  -v "$(pwd)/licenses/gurobi.lic:/app/licenses/gurobi.lic:ro" \
  -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic \
  metaoptimize:latest \
  --problemType BinPacking \
  --solver Gurobi
```

**Windows PowerShell:**
```powershell
docker run --rm `
  -v "$PWD/licenses/gurobi.lic:/app/licenses/gurobi.lic:ro" `
  -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic `
  metaoptimize:latest `
  --problemType BinPacking `
  --solver Gurobi
```

---

#### Option 3: Web License Service

**Linux/Mac:**
```bash
docker run --rm \
  -e GRB_WLSACCESSID=your-access-id \
  -e GRB_WLSSECRET=your-secret \
  -e GRB_LICENSEID=your-license-id \
  metaoptimize:latest \
  --problemType BinPacking \
  --solver Gurobi
```

**Windows PowerShell:**
```powershell
docker run --rm `
  -e GRB_WLSACCESSID=your-access-id `
  -e GRB_WLSSECRET=your-secret `
  -e GRB_LICENSEID=your-license-id `
  metaoptimize:latest `
  --problemType BinPacking `
  --solver Gurobi
```

---

### Supported Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `GRB_TOKEN_SERVER` | Token server address (host:port) | `10.137.58.158:41954` |
| `GRB_LICENSE_FILE` | Path to license file inside container | `/app/licenses/gurobi.lic` |
| `GRB_WLSACCESSID` | Web License Service access ID | `your-access-id` |
| `GRB_WLSSECRET` | Web License Service secret | `your-secret` |
| `GRB_LICENSEID` | Web License Service license ID | `your-license-id` |

**Note:** Only one license method is required. Set GRB_TOKEN_SERVER **OR** GRB_LICENSE_FILE **OR** all three WLS variables.

---

## Building and Deployment

### Local Build

```bash
# Build the image
docker build -t metaoptimize:latest .

# Build without cache (clean build)
docker build --no-cache -t metaoptimize:latest .

# Build with docker-compose
docker compose build
```

---

### MSRHub Deployment

```bash
# Build with MSRHub registry tag
docker build -t msrhub.azurecr.io/metaoptimize:latest .

# Login to Azure Container Registry
az acr login --name msrhub

# Push to registry
docker push msrhub.azurecr.io/metaoptimize:latest
```

**CI/CD Pipeline:** The `build-remote-project.yaml` Azure DevOps pipeline automates:
1. Cloning from GitHub (`microsoft/MetaOpt`, branch `metaopt-main`)
2. Building the Docker image
3. Pushing to `msrhub.azurecr.io`
4. Running Trivy security scan

**MSRHub Administrator Setup (One-Time):**
1. SSH to MSRHub server
2. Set Gurobi environment variable: `export GRB_TOKEN_SERVER="10.137.58.158:41954"`
3. Make it persistent: Add to `/etc/environment`
4. Deploy MetaOptimize container
5. Containers automatically use Gurobi when `--solver Gurobi` is specified

---

## Advanced Topics

### Deterministic vs. Non-Deterministic Results

**Deterministic (default):**
- Gurobi runs single-threaded (`--gurobithreads 1`)
- Produces identical results on repeated runs
- Slower but reproducible for research

**Non-Deterministic (faster):**
```bash
docker run --rm \
  metaoptimize:latest \
  --problemType BinPacking \
  --solver Gurobi \
  --gurobithreads 4
```

Results may vary between runs due to parallel algorithm race conditions.

---

### Custom Topology Files

**Topology Format (JSON):**
```json
{
  "nodes": [
    {"id": "a"},
    {"id": "b"},
    {"id": "c"}
  ],
  "links": [
    {"source": "a", "target": "b", "capacity": 10},
    {"source": "b", "target": "c", "capacity": 15},
    {"source": "a", "target": "c", "capacity": 5}
  ]
}
```

**Mount Custom Topologies - Linux/Mac:**
```bash
docker run --rm \
  -v "$(pwd)/my-topologies:/app/Topologies:ro" \
  metaoptimize:latest \
  --problemType TrafficEngineering \
  --topologyFile Topologies/my-network.json \
  --heuristic Pop
```

**Mount Custom Topologies - Windows PowerShell:**
```powershell
docker run --rm `
  -v "$PWD/my-topologies:/app/Topologies:ro" `
  metaoptimize:latest `
  --problemType TrafficEngineering `
  --topologyFile Topologies/my-network.json `
  --heuristic Pop
```

---

### Resource Limits

**Increase Memory for Large Problems:**
```bash
docker run --rm -m 8g \
  metaoptimize:latest \
  --problemType BinPacking \
  --numBins 20 \
  --numDemands 30
```

**Set CPU Limits:**
```bash
docker run --rm --cpus="4.0" \
  metaoptimize:latest \
  --problemType TrafficEngineering \
  --solver Gurobi \
  --gurobithreads 4
```

---

## Troubleshooting

### Gurobi License Errors

**Error:**
```
ERROR: Gurobi solver selected but no license configuration found.
```

**Solutions:**

1. **For MSRHub users:** Contact your administrator to verify Gurobi license is configured on the server
2. **For local development:** Set one of these environment variables:
   - `GRB_TOKEN_SERVER=hostname:port`
   - `GRB_LICENSE_FILE=/path/to/gurobi.lic`
   - `GRB_WLSACCESSID` + `GRB_WLSSECRET` + `GRB_LICENSEID`
3. **Alternative:** Use the free solver: `--solver Zen`

**Verify environment variable:**
```bash
# Linux/Mac
echo $GRB_TOKEN_SERVER

# Windows PowerShell
echo $env:GRB_TOKEN_SERVER

# Inside container
docker run --rm metaoptimize:latest env | grep GRB
```

---

**Error:**
```
Gurobi license validation failed
Failed to connect to token server
```

**Solutions:**
1. Verify token server is accessible: `ping 10.137.58.158`
2. Check port is not blocked: `telnet 10.137.58.158 41954`
3. Verify license server is running
4. Contact Gurobi administrator

---

### Topology File Not Found

**Error:**
```
FileNotFoundError: Topology file not found: myfile.json
```

**Solutions:**
1. Mount topology directory: `-v "$(pwd)/Topologies:/app/Topologies:ro"` (Linux/Mac) or `-v "$PWD/Topologies:/app/Topologies:ro"` (PowerShell)
2. Use correct path: `--topologyFile Topologies/myfile.json` (include `Topologies/` prefix)
3. Verify file exists on host: `ls Topologies/myfile.json`
4. Check file permissions (must be readable)

---

### Infeasible Model

**Error:**
```
ERROR: model not optimal infeasible
```

**Causes and Solutions:**

| Problem Type | Likely Cause | Solution |
|--------------|--------------|----------|
| BinPacking | Too few bins for optimal count | Increase `--numBins` |
| PIFO | Parameter constraints too tight | Use default parameters (18 packets, 8 ranks, 4 queues) |
| TrafficEngineering | Topology not connected | Verify topology has paths between all node pairs |
| FailureAnalysis | Too many simultaneous failures | Reduce `--maxNumFailures` |

**Debug Steps:**
1. Enable verbose output: `--verbose`
2. Try with default parameters first
3. Check input file format (for TrafficEngineering/FailureAnalysis)
4. Verify problem parameters are mathematically feasible

---

### Out of Memory

**Error:**
```
ERROR: std::bad_alloc
Killed
```

**Solutions:**
1. Increase container memory: `docker run --rm -m 8g ...`
2. Reduce problem size:
   - BinPacking: Decrease `--numBins` or `--numDemands`
   - PIFO: Decrease `--numPackets` or `--numQueues`
   - TrafficEngineering: Use simpler topology
3. Add timeout: `--timeout 600` (stops after 10 minutes)

---

### Slow Performance

**Symptoms:**
- Solver runs for minutes/hours without completing
- High CPU usage

**Solutions:**

1. **Set a timeout:**
   ```bash
   --timeout 300  # Stop after 5 minutes
   ```

2. **Enable multi-threading (Gurobi only, trades determinism for speed):**
   ```bash
   --solver Gurobi --gurobithreads 4
   ```

3. **Reduce problem complexity:**
   - Use fewer bins, packets, or nodes
   - Reduce `--paths` for TrafficEngineering
   - Use `Direct` method instead of `Search`

4. **Try Gurobi (if available):**
   ```bash
   --solver Gurobi  # Generally faster than OR-Tools
   ```

---

### Boolean Parameter Errors

**Error:**
```
ERROR(S):
  Option 'useDefaultTopology' has no value.
```

**Cause:** Boolean parameters require explicit values in MetaOptimize.

**Incorrect:**
```bash
--useDefaultTopology          # ❌ Missing value
--breakSymmetry               # ❌ Missing value
```

**Correct:**
```bash
--useDefaultTopology true     # ✅ Explicit value
--breakSymmetry false         # ✅ Explicit value
```

**Exception:** Flag-only parameters like `--verbose` and `--debug` don't take values:
```bash
--verbose                     # ✅ Correct (flag only)
--debug                       # ✅ Correct (flag only)
```

---

### PowerShell Line Continuation Errors

**Error:**
```
docker: invalid reference format
```

**Cause:** Using bash backslash `\` in PowerShell instead of backtick `` ` ``

**Incorrect (bash syntax in PowerShell):**
```powershell
docker run --rm \           # ❌ Wrong
  -v "$PWD/licenses:..." \  # ❌ Wrong
  metaoptimize:latest
```

**Correct (PowerShell syntax):**
```powershell
docker run --rm `           # ✅ Backtick
  -v "$PWD/licenses:..." `  # ✅ Backtick
  metaoptimize:latest
```

**Or use one line:**
```powershell
docker run --rm -v "$PWD/licenses:/app/licenses:ro" -e GRB_LICENSE_FILE=/app/licenses/gurobi.lic metaoptimize:latest
```

---

## Examples

### Example 1: Traffic Engineering with Swan Topology (Free Solver)

```bash
docker compose run --rm te-swan
```

**Output:**
```
Exploring pop heuristic
RESULTS:
Optimal: 120
Heuristic: 80
Gap: 40
Time: 1234ms
```

---

### Example 2: Bin Packing with Gurobi (PowerShell)

```powershell
# Requires GRB_TOKEN_SERVER environment variable set on server
docker run --rm `
  metaoptimize:latest `
  --problemType BinPacking `
  --solver Gurobi `
  --numBins 8 `
  --numDemands 12 `
  --breakSymmetry true `
  --verbose
```

---

### Example 3: PIFO with Custom Parameters (Free Solver)

```bash
docker run --rm \
  metaoptimize:latest \
  --problemType PIFO \
  --numPackets 24 \
  --maxRank 10 \
  --numQueues 6 \
  --timeout 600 \
  --verbose
```

---

### Example 4: Failure Analysis with Multiple Failures

```bash
docker compose run --rm failure-multi
```

---

## Getting Help

```bash
# Show all parameters
docker compose run --rm metaopt --help

# Show version
docker compose run --rm metaopt --version
```

**For issues or questions:**
- Check this README's Troubleshooting section
- Review parameter defaults with `--help`
- Test with free OR-Tools solver first: `--solver Zen`
- Verify Gurobi license configuration (if using Gurobi)

---

## Technical Details

**Base Image:** `mcr.microsoft.com/dotnet/sdk:8.0` (build), `mcr.microsoft.com/dotnet/runtime:8.0` (runtime)

**Gurobi Version:** 11.0.3 (optional)

**.NET Version:** 8.0

**Default Solver:** OR-Tools (Zen) - free, no license required

**Optional Solver:** Gurobi - requires license via environment variables

**Architecture:** Multi-stage build for minimal image size (~780MB)

**Default Working Directory:** `/app` (solution root)

**Entry Point:** `dotnet MetaOptimize.Cli.dll`

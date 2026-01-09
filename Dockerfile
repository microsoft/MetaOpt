# =============================================================================
# MetaOptimize Dockerfile
# =============================================================================

# Global ARGs
ARG BASE_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0-azurelinux3.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:8.0-azurelinux3.0
# mcr.microsoft.com/dotnet/runtime:8.0-azurelinux3.0
# msrhubroot.azurecr.io/cliwrapper:dotnet8.0 

# Build stage
FROM ${BASE_IMAGE} AS build

WORKDIR /src

# Copy build configuration files first (for layer caching)
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Copy solution and project files
COPY *.sln ./
COPY MetaOptimize/*.csproj MetaOptimize/
COPY MetaOptimize.Cli/*.csproj MetaOptimize.Cli/
COPY MetaOptimize.Test/*.csproj MetaOptimize.Test/

# Copy StyleCop props
COPY .stylecop/ .stylecop/

# Restore NuGet packages
RUN dotnet restore -r linux-x64

# Copy all source code
COPY . .

# Build and publish the CLI project in Release mode
RUN dotnet publish MetaOptimize.Cli/MetaOptimize.Cli.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish \
    --no-restore

# =============================================================================
# Runtime stage
# =============================================================================
FROM ${RUNTIME_IMAGE} AS runtime

# Install required packages
RUN tdnf install -y \
    ca-certificates \
    curl \
    tar \
    gzip \
    libstdc++ \
    && tdnf clean all

# Gurobi version
ARG GUROBI_VERSION=11.0.3

# Download and install Gurobi
RUN curl -L "https://packages.gurobi.com/11.0/gurobi${GUROBI_VERSION}_linux64.tar.gz" \
    -o /tmp/gurobi.tar.gz \
    && tar -xzf /tmp/gurobi.tar.gz -C /opt \
    && rm /tmp/gurobi.tar.gz \
    && mv /opt/gurobi1103 /opt/gurobi

# Set Gurobi environment variables
ENV GUROBI_HOME=/opt/gurobi
ENV PATH="${GUROBI_HOME}/bin:${PATH}"
ENV LD_LIBRARY_PATH="${GUROBI_HOME}/lib"

# Create application directories
WORKDIR /app
RUN mkdir -p /app/Topologies /app/output /app/licenses

# Copy published application
COPY --from=build /app/publish .

# Copy topology files
COPY Topologies/ /app/Topologies/

# Environment variables
ENV GUROBI_THREADS=1

# Volume mounts
VOLUME ["/app/Topologies", "/app/output", "/app/licenses"]

# Entry point
ENTRYPOINT ["dotnet", "MetaOptimize.Cli.dll"]
CMD ["--help"]

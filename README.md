# Halina

Halina is a research framework and experimental testbed for exploring the application of **Invertible Bloom Lookup Tables (IBLTs)** and probabilistic data structures to genomic data processing. It is written in C# and focuses on the efficient storage, retrieval, and reconstruction of K-mers (subsequences of DNA) and the detection of mutations.

## Features

*   **High-Performance K-mer Handling**: Efficient 2-bit encoding of nucleotides and K-mer representations.
*   **Invertible Bloom Lookup Tables (IBLT)**: Custom implementation of IBLTs for generic data and specialized K-mer data, supporting peeling decoding.
*   **Rolling Hash Algorithms**: Implementation of Tabulation Hashing and Rolling Hashes for fast K-mer traversal and reconstruction.
*   **Sequence Reconstruction**: Algorithms to reconstruct full K-mer sets from sparse samples using "pumping" (extending seeds via rolling hashes).
*   **Mutation Detection**: Experimental pipelines to identify mutations in DNA sequences using set difference techniques on compressed data structures.
*   **Configurable Experiments**: JSON-based configuration system to run batched experiments with varying parameters (K-mer size, table size, sampling rates, etc.).

## Project Structure

*   **`src/Halina.Core`**: The core library containing data structures (IBLT, Kmer), hashing algorithms, and sequence generation logic.
*   **`experiments/Halina.Experiments`**: Console application for running various research experiments.
*   **`bench/Halina.Benchmarks`**: Performance benchmarks using BenchmarkDotNet.
*   **`tests/Halina.Tests`**: Unit tests using xUnit.

## Getting Started

### Prerequisites

*   .NET 8.0 SDK (or compatible version).

### Building

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

## Usage

The main entry point for running experiments is the `Halina.Experiments` project.

```bash
dotnet run --project experiments/Halina.Experiments -- [mode] [config_path]
```

### Modes

1.  **`kmer`**: Runs basic K-mer encoding and decoding experiments to test IBLT capacity and correctness.
2.  **`mutation`**: Runs experiments focused on identifying mutations between sequences using IBLTs and H-mer hashing.
3.  **`hashset-extended`**: Runs the "HashSet Predictor" experiment, which attempts to reconstruct a full set of K-mers from a compressed IBLT and a sparse sample by "pumping" seeds.

### Configuration

If no configuration file is provided, the application will generate a default JSON configuration file for the selected mode (e.g., `kmer_config_default.json`) and run with default settings.

You can customize parameters such as:
*   `Seed`: Random seed(s).
*   `KmerLength`: Length of the K-mers (e.g., 31).
*   `NSequences`: Number of sequences to generate.
*   `SequenceLength`: Length of each sequence.
*   `K`, `L`: Sampling parameters for specific experiments.

Example `config.json` (you can use the supplied `experiments/Halina.Experiments/ex_conf_base.json` as a starting point):
```json
{
  "Path": "results",
  "Seed": { "Start": 123, "End": 123, "Multiplicative": true },
  "KmerLength": { "Start": 31, "End": 31 },
  "NSequences": { "Start": 1000, "End": 1000 }
}
```

## Benchmarks

To run performance benchmarks:

```bash
dotnet run --project bench/Halina.Benchmarks -c Release
```

## Running in Docker

Build the container once and keep the published artifacts isolated from the host.

```bash
docker build -t halina:latest .
```

When running experiments you can mount a directory containing your configuration files into `/app/config` (the container already ships with `ex_conf_base.json` in the repo) and pass the path you mounted as the config argument. Likewise mount a host directory to `/app/results` so generated JSON outputs stay on the host. Update the `Path` entry in your mounted config (e.g., set it to `/app/results/out`) so the experiment writes directly into the mounted folder; after the run you'll find output JSON files in `/path/to/results/out` on the host.

```bash
docker run --rm \
  -v /path/to/configs:/app/config \
  -v /path/to/results:/app/results \
  halina:latest hashset-extended /app/config/ex_conf_base.json --parallel

If you prefer to keep edits inside the repo, copy `experiments/Halina.Experiments/ex_conf_base.json` to the mount target and pass that path (or override values in your own file but keep the same name so the README command keeps working).
```

Inside the container you can also inspect the default config (`hashset_extended_config_default.json`) before overriding it; anything you add to the mounted directory is available to the entry point so you can pick the desired JSON file by passing its path.
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

Example `config.json`:
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
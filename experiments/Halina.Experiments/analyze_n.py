import pandas as pd
import matplotlib.pyplot as plt
import glob
import re
import os
import argparse
import sys

def process_files(directory, output_csv, output_plot):
    results = []
    
    # Construct search pattern for data*.csv
    search_pattern = os.path.join(directory, "data*.csv")
    files = glob.glob(search_pattern)
    
    if not files:
        print(f"No files matching 'data*.csv' found in '{directory}'")
        return

    print(f"Found {len(files)} files.")

    for file_path in files:
        filename = os.path.basename(file_path)
        
        # Extract N from filename
        match = re.search(r'data(\d+)\.csv', filename)
        if not match:
            print(f"Skipping '{filename}': Does not match 'dataN.csv' pattern.")
            continue
        
        n_val = int(match.group(1))
        
        try:
            df = pd.read_csv(file_path)
        except Exception as e:
            print(f"Error reading '{filename}': {e}")
            continue

        # Check for required columns
        required_cols = ['K', 'L', 'AvgSuccessRate']
        if not all(col in df.columns for col in required_cols):
            print(f"Skipping '{filename}': Missing required columns.")
            continue

        # Filter for success rate >= 0.999
        valid_df = df[df['AvgSuccessRate'] >= 0.999].copy()
        
        if valid_df.empty:
            print(f"Skipping '{filename}': No results with AvgSuccessRate >= 0.999.")
            continue
            
        # Calculate Memory Consumption: 1/L + (1/K * 1.5)
        valid_df['MemoryConsumption'] = (1.0 / valid_df['L']) + (1.5 / valid_df['K'])
        
        # Find the row with the minimum MemoryConsumption
        best_idx = valid_df['MemoryConsumption'].idxmin()
        best_row = valid_df.loc[best_idx]
        
        results.append({
            'N': n_val,
            'BestK': int(best_row['K']),
            'BestL': int(best_row['L']),
            'MemoryConsumption': best_row['MemoryConsumption'],
            'TotalMemory': n_val * best_row['MemoryConsumption']
        })

    if not results:
        print("No valid data found after processing.")
        return

    # Create DataFrame and sort by N
    results_df = pd.DataFrame(results)
    results_df = results_df.sort_values('N')
    
    # Save to CSV
    try:
        results_df.to_csv(output_csv, index=False)
        print(f"Results saved to '{output_csv}'")
    except Exception as e:
        print(f"Error saving CSV: {e}")
    
    # Plotting
    plt.figure(figsize=(10, 6))
    plt.plot(results_df['N'], results_df['MemoryConsumption'], marker='o', linestyle='-')
    
    plt.title('Memory Consumption vs N (Success Rate >= 0.999)')
    plt.xlabel('N')
    plt.ylabel('Memory Consumption (1/L + 1.5/K)')
    plt.grid(True)
    
    try:
        plt.savefig(output_plot)
        print(f"Plot saved to '{output_plot}'")
    except Exception as e:
        print(f"Error saving plot: {e}")

    # Plotting Total Memory
    base_name, ext = os.path.splitext(output_plot)
    total_plot_path = f"{base_name}_total{ext}"

    plt.figure(figsize=(10, 6))
    plt.plot(results_df['N'], results_df['TotalMemory'], marker='s', linestyle='-', color='r')
    
    plt.title('Total Memory Consumption (N * per-item) vs N')
    plt.xlabel('N')
    plt.ylabel('Total Memory Consumption')
    plt.grid(True)
    
    try:
        plt.savefig(total_plot_path)
        print(f"Total memory plot saved to '{total_plot_path}'")
    except Exception as e:
        print(f"Error saving total memory plot: {e}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Analyze memory consumption vs N from dataN.csv files.")
    parser.add_argument("directory", nargs='?', default=".", help="Directory containing the CSV files.")
    parser.add_argument("--output-csv", default="memory_vs_n.csv", help="Output CSV filename.")
    parser.add_argument("--plot", default="memory_vs_n.png", help="Output plot filename.")
    
    args = parser.parse_args()
    
    process_files(args.directory, args.output_csv, args.plot)
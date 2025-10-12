import pandas as pd
import matplotlib.pyplot as plt
import numpy as np

def plot_benchmark_results(filename="../results.csv"):
    """Reads the benchmark CSV and generates a simplified performance graph."""
    try:
        # อ่านข้อมูลจาก CSV ด้วย pandas
        df = pd.read_csv(filename)
    except FileNotFoundError:
        print(f"Error: '{filename}' not found. Please run the C# benchmark first.")
        return

    # แยกข้อมูลเพื่อความชัดเจน
    # เราใช้ข้อมูล Sequential จาก Type 'Sequential' ซึ่งถูกแมพไว้ที่ 1 thread ในไฟล์ CSV
    seq_df = df[df['Type'] == 'Sequential']
    if seq_df.empty:
        print("Error: Sequential run data not found in CSV.")
        return
    seq_time = seq_df['Time_ms'].iloc[0]

    # ข้อมูล Parallel คือส่วนที่เหลือ
    par_df = df[df['Type'] != 'Sequential'].sort_values(by='Threads')

    # --- เริ่มสร้างกราฟ ---
    # สร้าง Figure และ Axes แค่ชุดเดียว
    fig, ax = plt.subplots(figsize=(12, 7))
    fig.suptitle('Parallel Error Diffusion Performance', fontsize=16)

    # --- พล็อตกราฟ Execution Time ---
    ax.set_xlabel('Number of Threads')
    ax.set_ylabel('Execution Time (ms)')

    # พล็อตกราฟของ Parallel run
    ax.plot(par_df['Threads'], par_df['Time_ms'], marker='o', linestyle='-', label='Parallel Time')

    # เพิ่มเส้น Sequential Time เป็นเส้นประอ้างอิง
    ax.axhline(y=seq_time, color='red', linestyle='--', label=f'Sequential Time ({seq_time} ms)')

    # --- ตั้งค่าแกน X ---
    # บังคับให้แสดงเลขบนแกน X ครบทุกตัว
    thread_counts = par_df['Threads'].unique()
    ax.set_xticks(thread_counts)

    # ตั้งค่าอื่นๆ
    ax.grid(True, which='both', linestyle='--', linewidth=0.5)
    ax.legend()

    # จัดการ Layout, บันทึก และแสดงผล
    fig.tight_layout(rect=[0, 0, 1, 0.96])
    plt.savefig('performance_graph_simple.png')
    print("Graph saved to performance_graph_simple.png")
    plt.show()

if __name__ == '__main__':
    plot_benchmark_results()

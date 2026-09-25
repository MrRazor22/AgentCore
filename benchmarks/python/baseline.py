import time
import tracemalloc

def baseline():
    """Raw Python baseline: pure function call."""
    tracemalloc.start()
    start = time.perf_counter_ns()
    
    # Direct function execution
    def get_weather(city: str) -> str:
        return f"15C and sunny in {city}"
    
    res = get_weather("London")
    
    duration_ns = time.perf_counter_ns() - start
    current, peak = tracemalloc.get_traced_memory()
    tracemalloc.stop()
    
    print(f"[Baseline] Latency: {duration_ns / 1_000:.2f} us | Peak Memory: {peak / 1024:.2f} KB")

if __name__ == "__main__":
    baseline()

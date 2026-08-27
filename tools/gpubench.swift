// Is the GPU throttled? Times a fixed FMA workload on every Metal device and reports GFLOP/s.
// A Radeon Pro 5500M at full clocks manages ~2500-3500; a few hundred means the machine is
// power-capping the discrete GPU (low battery on an undersized charger) and no build will run well.
// Run: swiftc -O -o /tmp/gpubench tools/gpubench.swift && /tmp/gpubench   (or: make gpubench)
import Metal
import Foundation
let src = """
#include <metal_stdlib>
using namespace metal;
kernel void burn(device float *out [[buffer(0)]], uint id [[thread_position_in_grid]]) {
    float a = float(id) * 1e-6f, b = 1.0001f;
    for (int i = 0; i < 2048; i++) { a = fma(a, b, 0.5f); b = fma(b, a, 0.25f); }
    out[id] = a + b;
}
"""
let devices = MTLCopyAllDevices()
for dev in devices {
    let lib = try! dev.makeLibrary(source: src, options: nil)
    let fn = lib.makeFunction(name: "burn")!
    let pso = try! dev.makeComputePipelineState(function: fn)
    let q = dev.makeCommandQueue()!
    let n = 1 << 20
    let buf = dev.makeBuffer(length: n * 4, options: .storageModeShared)!
    func run() -> Double {
        let cb = q.makeCommandBuffer()!
        let enc = cb.makeComputeCommandEncoder()!
        enc.setComputePipelineState(pso); enc.setBuffer(buf, offset: 0, index: 0)
        enc.dispatchThreads(MTLSize(width: n, height: 1, depth: 1), threadsPerThreadgroup: MTLSize(width: 256, height: 1, depth: 1))
        enc.endEncoding()
        let t = Date(); cb.commit(); cb.waitUntilCompleted()
        return Date().timeIntervalSince(t) * 1000
    }
    _ = run(); _ = run()
    var best = 1e9, total = 0.0
    for _ in 0..<10 { let ms = run(); best = min(best, ms); total += ms }
    let gflop = Double(n) * 2048 * 2 * 2 / 1e9
    print("\(dev.name): best \(String(format: "%.1f", best)) ms/dispatch, mean \(String(format: "%.1f", total/10)) ms  → \(String(format: "%.0f", gflop / (best/1000))) GFLOP/s")
}

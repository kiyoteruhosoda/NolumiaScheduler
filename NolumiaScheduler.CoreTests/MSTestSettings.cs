// Each test method runs on its own test-class instance and rebuilds its state in Setup(),
// so method-level parallelization is safe and keeps the matrix fast.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

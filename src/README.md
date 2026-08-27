# Layout

```
src/TaskGuide.Domain          records + pure functions. No I/O, no framework, no package references.
src/TaskGuide.Application     ports and use cases. References Domain only.
src/TaskGuide.Infrastructure  JSON store, Pushover, weather, Serilog. References Application.
src/TaskGuide.Api             Minimal APIs (a MapGroup per feature) + the tick BackgroundService.
src/TaskGuide.Web             React/Vite SPA, built into TaskGuide.Api/wwwroot.
```

The dependency arrow points inward only, which is what makes the fan-out in
`tests/TEST-INVENTORY.md` possible: everything worth parallelising is a pure function in `Domain`.

**Emitted by [Spec assembly](https://github.com/jpjerkins/task-guide/issues/41) and never compiled.**
Every method body is `throw new NotImplementedException()` on purpose. The
[walking skeleton](https://github.com/jpjerkins/task-guide/issues/51) is what makes this build, run
and deploy; this asserts the shape, that one proves it.

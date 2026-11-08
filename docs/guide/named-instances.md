# Named Instances

Give an instance a name to keep it, data included, and share it across runs, test projects and tools:

snippet: sample_named_instance

- A named instance lives in the [shared cache](./cache#the-shared-cache) (`instances/app`), so every test run,
  app and tool on the machine finds the same one. Pass `DbDir` to keep it somewhere else instead.
- **If it is already running**, for example from an earlier run, another test project or the
  [pgembed CLI](https://mysticmind.github.io/pgembed-cli/), `StartAsync()` **attaches** to it in milliseconds
  instead of starting another server (`server.IsAttached`). Set `AttachIfRunning = false` to get an error
  instead.
- **Only the process that started a server stops it on dispose.** A server you attached to keeps running for
  whoever started it. `StopAsync()` and `KillAsync()` stop it either way.
- Two processes starting the same name at once get one server: a lock per instance makes the second attach.
- Its data is kept when the server stops. Pin at least the major version: see
  [Versions](./versions#pin-the-major-for-kept-data).

## Check or control a server

From anywhere, including a server another process started:

snippet: sample_instance_status

## InstanceId

A fixed `InstanceId` is the older way to reuse an instance and still works. Prefer `InstanceName`, and set one
or the other. Either way the instance is set up on the first run and only started afterwards.

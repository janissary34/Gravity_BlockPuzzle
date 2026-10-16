# Known Issues

Only verified unresolved issues belong here. Update or remove an entry in the same change that resolves it.

## Runtime-created reveal and pin objects

Status: architectural debt; current behaviour is functional.

Board background, frame, map collision, and obstacles now use the authored
`LevelRuntimeRoot`. Generated reveal presentations and pin objects still retain
their older runtime-construction paths.

Do not extend those paths. Migrate them to authored or prewarmed adapters in a
separate change while preserving reveal lifecycle ownership and pin collision
behaviour.

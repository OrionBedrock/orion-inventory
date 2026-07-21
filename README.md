# Orion Inventory

Opt-in player inventory (hotbar, main inventory, cursor, `ItemStackRequest` handling).

- **Manifest id:** `orion:inventory`
- **Provides:** `orion:inventory`
- **Depend:** `orion:containers` (project reference at build time; deploy both plugins)

## Build

```bash
# Build orion:containers first, then:
dotnet build OrionInventory.csproj -c Release
```

Deploy `plugin.json` and `orion.inventory.dll` under `plugins/orion:inventory/`.

## API

```csharp
if (context.Services.TryGet(out IInventoryApi? api) && api is not null)
{
    _ = api.Inventory.TryGive(player, stack, out _);
}
```

Registered services: `IInventoryApi`, `IPlayerInventoryService`.

Core dispatches a cancelable `PlayerOpenInventorySignal` before opening the inventory UI (E key).

## CI

GitHub Actions checks out `orion-containers`, builds it, then builds this plugin and smoke-boots the server with both plugins deployed.

Integration behavior is covered by smoke boot; unit tests validate NuGet `PackageReference` usage (no monorepo paths).

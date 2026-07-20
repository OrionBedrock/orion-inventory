namespace OrionInventory;

using Orion.Api;
using Orion.Api.Items;
using Orion.Api.Traits;
using Orion.Containers;
using OrionContainers;
using Orion.Protocol.Nbt;

/// <summary>
/// The single-slot "cursor" container: the item held on the mouse while an
/// inventory UI is open. Api-only player trait (no Orion.dll reference).
/// </summary>
public sealed class PlayerCursorTrait : PlayerTraitBase
{
    public new static string Identifier => "cursor";
    public static readonly string[] Types = ["minecraft:player"];

    public IEntity Entity { get; }
    public EntityContainer Container { get; }

    public PlayerCursorTrait(IEntity entity)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        Container = new EntityContainer(entity, ContainerType.Inventory, 1)
        {
            Identifier = 124
        };
    }

    public void OnSpawn() => Container.Update();

    public PlayerCursorTrait Clone(IEntity entity)
    {
        PlayerCursorTrait clone = new(entity);
        if (Container.GetItem(0) is { } item)
        {
            clone.Container.SetItem(0, item);
        }

        return clone;
    }

    public void OnRead(CompoundTag tag)
    {
        CompoundTag? containerTag = tag.Get<CompoundTag>("container");
        if (containerTag is null)
        {
            return;
        }

        Container.Deserialize(containerTag);
    }

    public void OnWrite(CompoundTag tag)
    {
        tag.Set("container", Container.Serialize());
    }
}

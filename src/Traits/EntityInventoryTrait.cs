namespace OrionInventory;

using Orion.Api;
using Orion.Api.Items;
using Orion.Api.Network;
using Orion.Api.Traits;
using Orion.Containers;
using OrionContainers;
using Orion.Protocol.Enums;
using Orion.Protocol.Nbt;
using Orion.Protocol.Packets;
using Orion.Protocol.Types;

/// <summary>
/// Entity inventory storage. Runs on players (36 slots) and containers (27 slots).
/// Api-only: subclasses <see cref="EntityTraitBase"/> and never touches Orion.dll.
/// </summary>
public sealed class EntityInventoryTrait : EntityTraitBase
{
    public new static string Identifier => "inventory";
    public static readonly string[] Types = ["minecraft:player"];
    public static readonly string[] Components = ["minecraft:inventory"];

    public IEntity Entity { get; }
    public EntityContainer Container { get; }
    public int SelectedSlot { get; private set; }
    public bool Opened { get; private set; }

    public EntityInventoryTrait(IEntity entity)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        bool playerInventory = entity.IsPlayer();

        Container = new EntityContainer(
            entity,
            playerInventory ? ContainerType.Inventory : ContainerType.Container,
            playerInventory ? 36 : 27)
        {
            Identifier = 0
        };
    }

    public IItemStack? GetHeldItem() => Container.GetItem(SelectedSlot);

    public void SetHeldItem(int slot)
    {
        if (slot >= 0 && slot < Container.GetSize())
        {
            SelectedSlot = slot;
        }
    }

    public void Clear()
    {
        Container.Clear();

        if (Entity is not IPlayer player || !player.Spawned)
        {
            return;
        }

        InventoryContentPacket packet = new()
        {
            WindowId = (uint)(Container.Identifier ?? 0),
            Content = Enumerable.Repeat(new NetworkItemStackDescriptor(), Container.GetSize()).ToList(),
            Container = new FullContainerName { ContainerId = (byte)ContainerId.Inventory },
            StorageItem = new NetworkItemStackDescriptor()
        };

        player.Send(new OpaqueOutboundPacket(packet));
    }

    public override void OnTick(TraitOnTickDetails details)
    {
        bool hasViewers = Container.GetAllOccupants().Count > 0;

        if (hasViewers == Opened)
        {
            return;
        }

        Opened = hasViewers;
    }

    public void OnSpawn()
    {
        if (Entity is IPlayer player)
        {
            Container.Show(player);
            Container.Update();
        }
    }

    public void OnRead(CompoundTag tag)
    {
        SelectedSlot = Math.Clamp(
            tag.Get<IntTag>("selected_slot")?.Value ?? SelectedSlot,
            0,
            Container.GetSize() - 1);

        CompoundTag? containerTag = tag.Get<CompoundTag>("container");
        if (containerTag is null)
        {
            return;
        }

        Container.Deserialize(containerTag);
    }

    public void OnWrite(CompoundTag tag)
    {
        tag.Set("selected_slot", new IntTag { Value = SelectedSlot });
        tag.Set("container", Container.Serialize());
    }

    public EntityInventoryTrait Clone(IEntity entity)
    {
        EntityInventoryTrait clone = new(entity)
        {
            SelectedSlot = SelectedSlot
        };

        for (int slot = 0; slot < Container.GetSize(); slot++)
        {
            IItemStack? item = Container.GetItem(slot);
            if (item is not null)
            {
                clone.Container.SetItem(slot, item);
            }
        }

        return clone;
    }

    public void SyncToPlayer(IPlayer player)
    {
        if (!player.Spawned)
        {
            return;
        }

        InventoryContentPacket packet = new()
        {
            WindowId = (uint)(Container.Identifier ?? 0),
            Content = new List<NetworkItemStackDescriptor>(Container.GetSize()),
            Container = new FullContainerName { ContainerId = (byte)ContainerId.Inventory },
            StorageItem = new NetworkItemStackDescriptor()
        };

        for (int i = 0; i < Container.GetSize(); i++)
        {
            packet.Content.Add(ItemNetwork.Describe(Container.GetItem(i)));
        }

        player.Send(new OpaqueOutboundPacket(packet));
    }

    public void SyncHeldItemToClient(IPlayer player)
    {
        byte hotBarSlot = SelectedSlot < 9 ? (byte)SelectedSlot : (byte)0;
        IItemStack? held = GetHeldItem();

        MobEquipmentPacket packet = new()
        {
            EntityRuntimeId = player.RuntimeId,
            InventorySlot = (byte)SelectedSlot,
            HotBarSlot = hotBarSlot,
            WindowId = 0,
            NewItem = ItemNetwork.Describe(held)
        };

        player.Send(new OpaqueOutboundPacket(packet));
    }
}

/// <summary>
/// Builds a wire descriptor from an <see cref="IItemStack"/> without host helpers.
/// Mirrors the minimal conversion in <c>Orion.Containers.Container</c>.
/// </summary>
internal static class ItemNetwork
{
    public static NetworkItemStackDescriptor Describe(IItemStack? item)
    {
        if (item is null || item.Type.NetworkId == 0 || item.Count == 0)
        {
            return new NetworkItemStackDescriptor();
        }

        return new NetworkItemStackDescriptor
        {
            NetworkId = item.Type.NetworkId,
            Count = (ushort)item.Count,
            Metadata = item.Metadata,
            StackNetworkId = item.NetworkStackId,
            BlockRuntimeId = 0,
            Nbt = null,
            CanPlaceOn = [],
            CanDestroy = [],
            BlockingTick = 0
        };
    }
}

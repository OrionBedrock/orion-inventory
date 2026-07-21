using Orion.Api;
using Orion.Api.Items;
using Orion.Containers;
using OrionContainers;
using Orion.Gameplay;
using Orion.Protocol.Enums;
using Orion.Protocol.Types;
using OrionInventory.Handlers;
using ApiContainer = Orion.Api.Containers.IContainer;
using ProtoItemStackRequest = Orion.Protocol.Types.ItemStackRequest;

namespace OrionInventory;

sealed class InventoryAccess(EntityInventoryTrait trait) : IPlayerInventoryAccess
{
    public ApiContainer Container => trait.Container;
    public int SelectedSlot => trait.SelectedSlot;
    public void SetHeldSlot(int slot) => trait.SetHeldItem(slot);
    public IItemStack? GetHeldItem() => trait.GetHeldItem();
    public void Clear() => trait.Clear();
    public void SyncToPlayer(IPlayer player) => trait.SyncToPlayer(player);
    public void SyncHeldItemToClient(IPlayer player) => trait.SyncHeldItemToClient(player);
}

public sealed class InventoryGameplayServices : IInventoryApi, IPlayerInventoryService
{
    public IPlayerInventoryService Inventory => this;

    public bool TryOpenInventory(IPlayer player)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return false;
        }

        inventory.Container.Show(player);
        return true;
    }

    public bool TryCloseInventory(IPlayer player, int windowId)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is not null && windowId == (inventory.Container.Identifier ?? 0))
        {
            inventory.Container.RemoveViewer(player, false);
            return true;
        }

        if (player.TryGetOpenContainer(windowId, out ApiContainer? open) && open is not null)
        {
            open.RemoveViewer(player, false);
            return true;
        }

        return false;
    }

    public bool TryGetAccess(IPlayer player, out IPlayerInventoryAccess? access)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            access = null;
            return false;
        }

        access = new InventoryAccess(inventory);
        return true;
    }

    public IItemStack? GetHeldItem(IPlayer player) =>
        player.GetTrait<EntityInventoryTrait>()?.GetHeldItem();

    public bool TrySetHeldSlot(IPlayer player, int slot)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return false;
        }

        inventory.SetHeldItem(slot);
        return true;
    }

    public bool TryGive(IPlayer player, IItemStack stack, out int leftover)
    {
        leftover = stack.Count;
        if (!TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        IItemStack remaining = stack.Clone();
        if (!access.Container.AddItem(remaining))
        {
            leftover = remaining.Count;
            return leftover < stack.Count;
        }

        leftover = 0;
        access.SyncToPlayer(player);
        return true;
    }

    public bool TryClear(IPlayer player)
    {
        if (!TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        access.Clear();
        return true;
    }

    public bool TryCollect(IPlayer player, IItemStack item, out ushort moved)
    {
        moved = 0;
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null || item.Count == 0)
        {
            return false;
        }

        Container container = inventory.Container;
        int remaining = item.Count;
        int movedCount = 0;

        for (int i = 0; i < container.GetSize() && remaining > 0; i++)
        {
            IItemStack? existing = container.GetItem(i);
            if (existing is null || !existing.CanStackWith(item) || existing.Count >= existing.Type.MaxStackSize)
            {
                continue;
            }

            int space = existing.Type.MaxStackSize - existing.Count;
            int transfer = Math.Min(space, remaining);
            if (transfer <= 0)
            {
                continue;
            }

            existing.Increment(transfer);
            container.UpdateSlot(i);
            remaining -= transfer;
            movedCount += transfer;
        }

        for (int i = 0; i < container.GetSize() && remaining > 0; i++)
        {
            if (container.GetItem(i) is not null)
            {
                continue;
            }

            int transfer = Math.Min(remaining, item.Type.MaxStackSize);
            IItemStack stack = item.Clone(transfer);
            container.SetItem(i, stack);
            remaining -= transfer;
            movedCount += transfer;
        }

        if (movedCount == 0)
        {
            return false;
        }

        item.SetCount(remaining);
        inventory.SyncToPlayer(player);
        moved = (ushort)movedCount;
        return true;
    }

    public bool TrySyncToClient(IPlayer player)
    {
        if (!player.Spawned || !TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        EnsureContainerViewer(player, access.Container, access.Container.Identifier ?? 0);
        access.SyncToPlayer(player);
        access.SyncHeldItemToClient(player);
        return true;
    }

    public void EnableHud(IPlayer player)
    {
        if (player.GetTrait<EntityInventoryTrait>() is null)
        {
            return;
        }

        player.SetHud(Orion.Api.HudVisibility.Reset, Orion.Api.HudElement.HotBar);
    }

    public ApiContainer? ResolveContainer(IPlayer player, ContainerNameWire name)
    {
        if (name.Value is not FullContainerName fullName)
        {
            return null;
        }

        return InventoryResolver.Resolve(player, fullName);
    }

    public bool TryProcessItemStackRequest(
        IPlayer player,
        ItemStackRequestWire request,
        out ItemStackResponseWire response)
    {
        if (request.Value is not ProtoItemStackRequest protocolRequest)
        {
            response = new ItemStackResponseWire(new ItemStackResponse
            {
                RequestId = 0,
                Status = ItemStackResponseStatus.Error
            });
            return false;
        }

        ItemStackResponse protocolResponse = ItemStackRequestHandler.Process(player, protocolRequest);
        response = new ItemStackResponseWire(protocolResponse);
        return true;
    }

    static void EnsureContainerViewer(IPlayer player, ApiContainer container, int windowId)
    {
        if (container is not Container concrete)
        {
            player.RegisterOpenContainer(windowId, container);
            return;
        }

        if (concrete.occupants.ContainsKey(player))
        {
            return;
        }

        concrete.occupants[player] = windowId;
        player.RegisterOpenContainer(windowId, concrete);
    }
}

/// <summary>
/// Resolves a client <see cref="FullContainerName"/> to a concrete plugin container,
/// using the player's inventory/cursor traits and any opened dynamic containers.
/// </summary>
internal static class InventoryResolver
{
    public static Container? Resolve(IPlayer player, FullContainerName fullName)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return null;
        }

        if (fullName.ContainerId is (byte)ContainerId.Armor or 12
            or (byte)ContainerId.Inventory or (byte)ContainerId.Hotbar
            or (byte)ContainerId.FixedInventory or (byte)ContainerId.Offhand)
        {
            return inventory.Container;
        }

        if (fullName.ContainerId is (byte)ContainerId.Cursor or (byte)ContainerId.CreatedOutput
            or (byte)ContainerName.Cursor or (byte)ContainerName.CreativeOutput)
        {
            return player.GetTrait<PlayerCursorTrait>()?.Container;
        }

        if (fullName.ContainerId == (byte)ContainerId.Barrel || fullName.ContainerId == (byte)ContainerId.InventoryUi
            || fullName.ContainerId == (byte)ContainerName.Barrel || fullName.ContainerId == (byte)ContainerName.Container)
        {
            if (fullName.DynamicContainerId.HasValue &&
                player.TryGetOpenContainer((int)fullName.DynamicContainerId.Value, out ApiContainer? containerById)
                && containerById is Container apiById)
            {
                return apiById;
            }

            foreach ((int _, ApiContainer candidate) in player.OpenedContainers)
            {
                if (candidate is Container concreteCandidate && concreteCandidate.Type != ContainerType.Inventory)
                {
                    return concreteCandidate;
                }
            }

            return inventory.Container;
        }

        ContainerName containerName = (ContainerName)fullName.ContainerId;
        switch (containerName)
        {
            case ContainerName.HotbarAndInventory:
            case ContainerName.Hotbar:
            case ContainerName.Inventory:
            case ContainerName.Armor:
            case ContainerName.Offhand:
                return inventory.Container;

            case ContainerName.Cursor:
            case ContainerName.CreativeOutput:
                return player.GetTrait<PlayerCursorTrait>()?.Container;
        }

        if (fullName.DynamicContainerId.HasValue &&
            player.TryGetOpenContainer((int)fullName.DynamicContainerId.Value, out ApiContainer? container)
            && container is Container api)
        {
            return api;
        }

        return null;
    }
}

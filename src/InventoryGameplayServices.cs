using Orion.Containers;
using Orion.Gameplay;
using Orion.Item;
using Orion.Player;
using Orion.Protocol.Enums;
using Orion.Protocol.Types;
using OrionInventory.Handlers;

namespace OrionInventory;

sealed class InventoryAccess(EntityInventoryTrait trait) : IPlayerInventoryAccess
{
    public IContainer Container => trait.Container;
    public int SelectedSlot => trait.SelectedSlot;
    public void SetHeldSlot(int slot) => trait.SetHeldItem(slot);
    public ItemStack? GetHeldItem() => trait.GetHeldItem();
    public void Clear() => trait.Clear();
    public void SyncToPlayer(Player player) => trait.SyncToPlayer(player);
    public void SyncHeldItemToClient(Player player) => trait.SyncHeldItemToClient(player);
}

public sealed class InventoryGameplayServices : IInventoryApi, IPlayerInventoryService
{
    public IPlayerInventoryService Inventory => this;

    public bool TryOpenInventory(Player player)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return false;
        }

        inventory.Container.Show(player);
        return true;
    }

    public bool TryCloseInventory(Player player, int windowId)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is not null && windowId == (inventory.Container.Identifier ?? 0))
        {
            inventory.Container.RemoveViewer(player, false);
            return true;
        }

        if (player.TryGetOpenContainer(windowId, out IContainer? open) && open is not null)
        {
            open.RemoveViewer(player, false);
            return true;
        }

        return false;
    }

    public bool TryGetAccess(Player player, out IPlayerInventoryAccess? access)
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

    public ItemStack? GetHeldItem(Player player) =>
        player.GetTrait<EntityInventoryTrait>()?.GetHeldItem();

    public bool TrySetHeldSlot(Player player, int slot)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return false;
        }

        inventory.SetHeldItem(slot);
        return true;
    }

    public bool TryGive(Player player, ItemStack stack, out int leftover)
    {
        leftover = stack.StackSize;
        if (!TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        ItemStack remaining = stack.Clone();
        if (!access.Container.AddItem(remaining))
        {
            leftover = remaining.StackSize;
            return leftover < stack.StackSize;
        }

        leftover = 0;
        access.SyncToPlayer(player);
        return true;
    }

    public bool TryClear(Player player)
    {
        if (!TryGetAccess(player, out IPlayerInventoryAccess? access) || access is null)
        {
            return false;
        }

        access.Clear();
        return true;
    }

    public bool TryCollect(Player player, ItemStack item, out ushort moved)
    {
        moved = 0;
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null || item.StackSize == 0)
        {
            return false;
        }

        Container container = inventory.Container;
        ushort remaining = item.StackSize;

        for (int i = 0; i < container.GetSize() && remaining > 0; i++)
        {
            ItemStack? existing = container.GetItem(i);
            if (existing is null || !existing.CanStackWith(item) || existing.StackSize >= existing.Type.MaxStackSize)
            {
                continue;
            }

            int space = existing.Type.MaxStackSize - existing.StackSize;
            int transfer = Math.Min(space, remaining);
            if (transfer <= 0)
            {
                continue;
            }

            existing.IncrementStack((ushort)transfer);
            container.UpdateSlot(i);
            remaining = (ushort)(remaining - transfer);
            moved = (ushort)(moved + transfer);
        }

        for (int i = 0; i < container.GetSize() && remaining > 0; i++)
        {
            if (container.GetItem(i) is not null)
            {
                continue;
            }

            ushort transfer = (ushort)Math.Min(remaining, item.Type.MaxStackSize);
            ItemStack stack = item.Clone(transfer);
            container.SetItem(i, stack);
            remaining = (ushort)(remaining - transfer);
            moved = (ushort)(moved + transfer);
        }

        if (moved == 0)
        {
            return false;
        }

        item.SetStackSize(remaining);
        inventory.SyncToPlayer(player);
        return true;
    }

    public bool TrySyncToClient(Player player)
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

    public void EnableHud(Player player)
    {
        if (player.GetTrait<EntityInventoryTrait>() is null)
        {
            return;
        }

        player.SetHud(HudVisibility.Reset, HudElement.HotBar);
    }

    public IContainer? ResolveContainer(Player player, FullContainerName name)
    {
        EntityInventoryTrait? inventory = player.GetTrait<EntityInventoryTrait>();
        if (inventory is null)
        {
            return null;
        }

        if (name.ContainerId is (byte)ContainerId.Armor or 12
            or (byte)ContainerId.Inventory or (byte)ContainerId.Hotbar
            or (byte)ContainerId.FixedInventory or (byte)ContainerId.Offhand)
        {
            return inventory.Container;
        }

        if (name.ContainerId is (byte)ContainerId.Cursor or (byte)ContainerId.CreatedOutput
            or (byte)ContainerName.Cursor or (byte)ContainerName.CreativeOutput)
        {
            return player.GetTrait<PlayerCursorTrait>()?.Container;
        }

        if (name.ContainerId == (byte)ContainerId.Barrel || name.ContainerId == (byte)ContainerId.InventoryUi
            || name.ContainerId == (byte)ContainerName.Barrel || name.ContainerId == (byte)ContainerName.Container)
        {
            if (name.DynamicContainerId.HasValue &&
                player.TryGetOpenContainer((int)name.DynamicContainerId.Value!, out IContainer? containerById))
            {
                return containerById;
            }

            foreach ((int _, IContainer candidate) in player.openedContainers)
            {
                if (candidate.Type != ContainerType.Inventory)
                {
                    return candidate;
                }
            }

            return inventory.Container;
        }

        ContainerName containerName = (ContainerName)name.ContainerId;
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

        if (name.DynamicContainerId.HasValue &&
            player.TryGetOpenContainer((int)name.DynamicContainerId.Value!, out IContainer? container))
        {
            return container;
        }

        return null;
    }

    public bool TryProcessItemStackRequest(Player player, ItemStackRequest request, out ItemStackResponse response)
    {
        response = ItemStackRequestHandler.Process(player, request);
        return true;
    }

    static void EnsureContainerViewer(Player player, IContainer container, int windowId)
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

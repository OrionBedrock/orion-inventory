using Orion.Api;
using Orion.Gameplay;
using Orion.PluginContracts;
using Orion.PluginContracts.Network;
using Orion.Protocol.Enums;
using OrionInventory.Handlers;

namespace OrionInventory;

public sealed class OrionInventoryPlugin : IOrionPlugin
{
    public string Id => "orion:inventory";

    public Version Version { get; } = new(1, 0, 0);

    public void Load(IPluginLoadContext context)
    {
        var assembly = typeof(OrionInventoryPlugin).Assembly;
        context.Registries.EntityTraits.RegisterFromAssembly(assembly, Id);
        context.Registries.PlayerTraits.RegisterFromAssembly(assembly, Id);
    }

    public void OnEnable(IPluginContext context)
    {
        InventoryGameplayServices services = new();
        context.Services.Register<IInventoryApi>(services, this);
        context.Services.Register<IPlayerInventoryService>(services, this);

        _ = context.Packets.TryOwnHandler((int)PacketId.ItemStackRequest, this, OwnItemStackRequest);
        _ = context.Packets.TryOwnHandler((int)PacketId.ContainerClose, this, OwnContainerClose);
        _ = context.Packets.TryOwnHandler((int)PacketId.MobEquipment, this, OwnMobEquipment);
    }

    public void OnWorldInitialize(IWorldInitContext context) => _ = context;

    public void OnDisable(IPluginContext context) => _ = context;

    static void OwnItemStackRequest(PacketReceiveContext ctx)
    {
        if (ctx.GetPlayer<IPlayer>() is not { } player)
        {
            return;
        }

        ItemStackRequestHandler.Handle(player, ctx.Payload.Span);
        ctx.Handled = true;
    }

    static void OwnContainerClose(PacketReceiveContext ctx)
    {
        if (ctx.GetPlayer<IPlayer>() is not { } player)
        {
            return;
        }

        ContainerCloseHandler.Handle(player, ctx.Payload.Span);
        ctx.Handled = true;
    }

    static void OwnMobEquipment(PacketReceiveContext ctx)
    {
        if (ctx.GetPlayer<IPlayer>() is not { } player)
        {
            return;
        }

        MobEquipmentHandler.Handle(player, ctx.Payload.Span);
        ctx.Handled = true;
    }
}

using Orion;
using Orion.Entity.Traits;
using Orion.Gameplay;
using Orion.PluginContracts;
using Orion.PluginContracts.Network;
using Orion.Plugins;
using Orion.Protocol.Enums;
using Orion.RakNet;
using OrionInventory.Handlers;

namespace OrionInventory;

public sealed class OrionInventoryPlugin : IOrionPlugin
{
    public string Id => "orion:inventory";

    public Version Version { get; } = new(1, 0, 0);

    public void Load(IPluginLoadContext context)
    {
        _ = context;
        EntityTraitRegistry.RegisterFromAssembly(typeof(OrionInventoryPlugin).Assembly);
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
        if (!TryGetServerConnection(ctx, out Server? server, out NetworkConnection? connection))
        {
            return;
        }

        ItemStackRequestHandler.Handle(server, connection, ctx.Payload.Span);
        ctx.Handled = true;
    }

    static void OwnContainerClose(PacketReceiveContext ctx)
    {
        if (!TryGetServerConnection(ctx, out Server? server, out NetworkConnection? connection))
        {
            return;
        }

        ContainerCloseHandler.Handle(server, connection, ctx.Payload.Span);
        ctx.Handled = true;
    }

    static void OwnMobEquipment(PacketReceiveContext ctx)
    {
        if (!TryGetServerConnection(ctx, out Server? server, out NetworkConnection? connection))
        {
            return;
        }

        MobEquipmentHandler.Handle(server, connection, ctx.Payload.Span);
        ctx.Handled = true;
    }

    static bool TryGetServerConnection(
        PacketReceiveContext ctx,
        out Server server,
        out NetworkConnection connection)
    {
        server = null!;
        connection = null!;
        if (ctx.Connection.Native is not NetworkConnection native
            || !PluginHost.TryGetServer(out Server? hostServer)
            || hostServer is null)
        {
            return false;
        }

        connection = native;
        server = hostServer;
        return true;
    }
}

using System;
using System.Linq;
using System.Numerics;
using Microsoft.Extensions.Logging;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.PublicEvent;
using NexusForever.Game.Static.Quest;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Entity
{
    public class ClientEntityInteractionHandler : IMessageHandler<IWorldSession, ClientEntityInteract>
    {
        #region Dependency Injection

        private readonly ILogger<ClientEntityInteractionHandler> log;

        private readonly IAssetManager assetManager;

        public ClientEntityInteractionHandler(
            ILogger<ClientEntityInteractionHandler> log,
            IAssetManager assetManager)
        {
            this.log          = log;
            this.assetManager = assetManager;
        }

        #endregion

        // the client can send an interaction for any visible entity (e.g. while falling past it); objectives are only
        // credited within this distance
        private const float ObjectiveInteractionRange = 15f;

        public void HandleMessage(IWorldSession session, ClientEntityInteract entityInteraction)
        {
            IWorldEntity entity = session.Player.GetVisible<IWorldEntity>(entityInteraction.Guid);
            if (entity != null && Vector3.Distance(session.Player.Position, entity.Position) > ObjectiveInteractionRange)
            {
                log.LogTrace($"Ignored objective credit for interaction with entity {entity.Guid} (creature {entity.CreatureId}), {Vector3.Distance(session.Player.Position, entity.Position):0.0} m away.");
            }
            else if (entity?.InteractionBlocked == true)
            {
                log.LogTrace($"Ignored objective credit for interaction with entity {entity.Guid} (creature {entity.CreatureId}), interaction blocked.");
            }
            else if (entity != null)
            {
                // the event scripts hear about it first, e.g. to play the NPC's line before the objective completes
                entity.Map.PublicEventManager.OnEntityInteract(session.Player, entity);

                session.Player.QuestManager.ObjectiveUpdate(QuestObjectiveType.ActivateEntity, entity.CreatureId, 1u);
                session.Player.QuestManager.ObjectiveUpdate(QuestObjectiveType.TalkTo, entity.CreatureId, 1u);

                foreach (uint targetGroupId in assetManager.GetTargetGroupsForCreatureId(entity.CreatureId) ?? Enumerable.Empty<uint>())
                {
                    session.Player.QuestManager.ObjectiveUpdate(QuestObjectiveType.TalkToTargetGroup, targetGroupId, 1u);
                    entity.Map.PublicEventManager.UpdateObjective(session.Player, PublicEventObjectiveType.TalkTo, targetGroupId, 1);
                }
            }

            switch (entityInteraction.Event)
            {
                case 37: // Quest NPC
                {
                    // only open a dialog when the NPC has something to show: an empty dialog is closed by the client right
                    // away (interaction event 101), and the NPC's speech bubble said at that moment (e.g. a public event
                    // TalkTo target answering) never shows
                    if (!HasDialog(entity))
                    {
                        log.LogTrace($"No dialog for entity {entityInteraction.Guid} (creature {entity?.CreatureId}), it has no quests or gossip.");
                        break;
                    }

                    session.EnqueueMessageEncrypted(new ServerDialogStart
                    {
                        DialogUnitId = entityInteraction.Guid
                    });
                    break;
                }
                case 49: // Handle Vendor
                    HandleVendor(session, entity);
                    break;
                case 68: // "MailboxActivate"
                    var mailboxEntity = session.Player.Map.GetEntity<IMailboxEntity>(entityInteraction.Guid);
                    break;
                case 8: // "HousingGuildNeighborhoodBrokerOpen"
                case 40:
                case 41: // "ResourceConversionOpen"
                case 42: // "ToggleAbilitiesWindow"
                case 43: // "InvokeTradeskillTrainerWindow"
                case 45: // "InvokeShuttlePrompt"
                case 46:
                case 47:
                case 48: // "InvokeTaxiWindow"
                case 65: // "MannequinWindowOpen"
                case 66: // "ShowBank"
                case 67: // "ShowRealmBank"
                case 69: // "ShowDye"
                case 70: // "GuildRegistrarOpen"
                case 71: // "WarPartyRegistrarOpen"
                case 72: // "GuildBankerOpen"
                case 73: // "WarPartyBankerOpen"
                case 75: // "ToggleMarketplaceWindow"
                case 76: // "ToggleAuctionWindow"
                case 79: // "TradeskillEngravingStationOpen"
                case 80: // "HousingMannequinOpen"
                case 81: // "CityDirectionsList"
                case 82: // "ToggleCREDDExchangeWindow"
                case 84: // "CommunityRegistrarOpen"
                case 85: // "ContractBoardOpen"
                case 86: // "BarberOpen"
                case 87: // "MasterCraftsmanOpen"
                default:
                    log.LogWarning($"Received unhandled interaction event {entityInteraction.Event} from Entity {entityInteraction.Guid}");
                    break;
            }
        }

        private static bool HasDialog(IWorldEntity entity)
        {
            // unknown creature data: keep the previous behaviour
            if (entity?.CreatureInfo?.Entry is not { } creature)
                return true;

            return creature.GossipSetId != 0u
                || creature.QuestIdGiven.Any(q => q != 0u)
                || creature.QuestIdReceive.Any(q => q != 0u);
        }

        private void HandleVendor(IWorldSession session, IWorldEntity worldEntity)
        {
            if (worldEntity is not INonPlayerEntity vendorEntity)
                throw new InvalidOperationException();

            if (vendorEntity.VendorInfo == null)
                throw new InvalidOperationException();

            session.Player.SelectedVendorInfo = vendorEntity.VendorInfo;

            ServerVendorItemsUpdated vendorItemsUpdated = vendorEntity.VendorInfo.Build();
            vendorItemsUpdated.Guid = vendorEntity.Guid;
            session.EnqueueMessageEncrypted(vendorItemsUpdated);
        }
    }
}

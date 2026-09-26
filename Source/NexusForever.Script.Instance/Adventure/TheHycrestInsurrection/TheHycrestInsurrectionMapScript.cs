using NexusForever.Script.Template.Filter;

namespace NexusForever.Script.Instance.Adventure.TheHycrestInsurrection
{
    // TODO: the automatic mission vote is test-only. It currently starts from a test trigger created in
    // TheHycrestInsurrectionEventScript (VoteTestTriggerId). The real trigger is completing intro event 418
    // (meeting Vesna Taranoft at the Abandoned Barn, WorldLocation2 13091). See HYCREST.md, "Vote system test".
    [ScriptFilterOwnerId(1149)]
    public class TheHycrestInsurrectionMapScript : EventBaseContentMapScript
    {
        public override uint PublicEventId => 419u;
    }
}

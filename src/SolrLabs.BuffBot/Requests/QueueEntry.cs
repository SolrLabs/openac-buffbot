namespace SolrLabs.BuffBot.Requests;

internal readonly record struct QueueEntry( // ObjectId maps the panel's selected index back to a person
    uint ObjectId, string Name, string Archetype, double WaitingSeconds);

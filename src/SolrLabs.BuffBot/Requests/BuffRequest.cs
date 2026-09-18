namespace SolrLabs.BuffBot.Requests;

internal readonly record struct BuffRequest(uint RequesterObjectId, string RequesterName, string SetName);

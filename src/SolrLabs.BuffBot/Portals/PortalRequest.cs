namespace SolrLabs.BuffBot.Portals;

internal readonly record struct PortalRequest(uint RequesterObjectId, string RequesterName, PortalTieSlot Slot);

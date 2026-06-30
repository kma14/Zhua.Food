namespace Zhua.Application.Review;

/// <summary>
/// Merge one item into another — POST /items/{id}/merge (rework phase 4). The source item's products + match
/// candidates are repointed to <c>IntoId</c> and the source becomes a redirect tombstone (the matcher resolves its
/// key to the survivor, so it isn't recreated). Non-destructive: store listings are never touched.
/// </summary>
public sealed record MergeItemRequest(Guid IntoId);

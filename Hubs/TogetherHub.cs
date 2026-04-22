using Microsoft.AspNetCore.SignalR;

namespace together_api.Hubs;

public class TogetherHub : Hub
{
    public async Task JoinPost(int postId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"post-{postId}");

    public async Task LeavePost(int postId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"post-{postId}");

    public async Task JoinFeed() =>
        await Groups.AddToGroupAsync(Context.ConnectionId, "feed");
}

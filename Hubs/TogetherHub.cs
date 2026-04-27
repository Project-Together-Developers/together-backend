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

    // Личный чат — группа по отсортированной паре userId
    public async Task JoinDM(int myId, int friendId)
    {
        var group = $"dm-{Math.Min(myId, friendId)}-{Math.Max(myId, friendId)}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveDM(int myId, int friendId)
    {
        var group = $"dm-{Math.Min(myId, friendId)}-{Math.Max(myId, friendId)}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    // Личный канал для уведомлений
    public async Task JoinUser(int userId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
}

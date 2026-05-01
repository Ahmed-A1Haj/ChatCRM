using Microsoft.AspNetCore.SignalR;

namespace ChatCRM.Infrastructure.Services
{
    public class ChatHub : Hub
    {
        /// <summary>
        /// Canonical name for the SignalR group that receives events for a given WhatsApp instance.
        /// </summary>
        public static string InstanceGroupName(int instanceId) => $"instance-{instanceId}";

        /// <summary>
        /// Group all authenticated clients join so they receive instance lifecycle events
        /// (created, status changed, disconnected) regardless of which instance they're viewing.
        /// </summary>
        public const string InstancesGlobalGroup = "instances-global";

        public override async Task OnConnectedAsync()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, InstancesGlobalGroup);
            await base.OnConnectedAsync();
        }

        public async Task JoinInstance(int instanceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, InstanceGroupName(instanceId));
        }

        public async Task LeaveInstance(int instanceId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, InstanceGroupName(instanceId));
        }
    }
}

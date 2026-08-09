using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace RadioPad.Api.Hubs;

public class ReportingHub : Hub
{
    public async Task JoinReportGroup(string reportId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"report-{reportId}");
    }

    public async Task LeaveReportGroup(string reportId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"report-{reportId}");
    }
}

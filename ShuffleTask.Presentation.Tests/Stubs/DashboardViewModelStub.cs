using System.Threading.Tasks;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels
{
    public class DashboardViewModel
    {
        public Task ApplyAutoOrCrossDeviceShuffleAsync(TaskItem task, AppSettings settings)
        {
            return Task.CompletedTask;
        }
    }
}

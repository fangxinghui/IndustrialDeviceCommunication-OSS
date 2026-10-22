using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    public static class ControllerFactory
    {
        public static (IController controller, IConfiguration configuration) TryCreateController(ControllerConfiguration? configuration)
        {
            if (configuration is null)
                return (null!, null!);

            var controller = ControllerDriverScanner.TryCreate(
                configuration.BrandName,
                configuration.Model,
                configuration.ControllerType,
                configuration);

            if (controller is null)
                return (null!, null!);

            return (controller, controller.CreateConfiguration()!);
        }
    }
}

using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace ArchieCopilot
{
    [Transaction(TransactionMode.Manual)]
    public class App : IExternalApplication
    {
        public static ExternalEvent? RevitExternalEvent { get; private set; }
        public static RevitCommandHandler? CommandHandler { get; private set; }

        private static readonly Guid PaneGuid = new("C4A72B5E-8F3D-4E9A-B1C6-5D2E7F8A9B0C");

        public Result OnStartup(UIControlledApplication application)
        {
            // Create the external event handler for safe Revit API execution
            CommandHandler = new RevitCommandHandler();
            RevitExternalEvent = ExternalEvent.Create(CommandHandler);

            // Register the dockable chat panel
            var paneId = new DockablePaneId(PaneGuid);
            var chatPanel = new ChatPanel();
            application.RegisterDockablePane(paneId, "Archie Copilot", chatPanel);

            // Create ribbon tab and button
            string tabName = "Archie Copilot";
            application.CreateRibbonTab(tabName);

            var panel = application.CreateRibbonPanel(tabName, "Commands");

            var buttonData = new PushButtonData(
                "ShowChatPanel",
                "Chat\nPanel",
                Assembly.GetExecutingAssembly().Location,
                "ArchieCopilot.ShowChatPanelCommand"
            );
            buttonData.ToolTip = "Show the Archie Copilot chat panel";

            panel.AddItem(buttonData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        public static DockablePaneId GetPaneId() => new DockablePaneId(PaneGuid);
    }

    [Transaction(TransactionMode.Manual)]
    public class ShowChatPanelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var paneId = App.GetPaneId();
            var pane = commandData.Application.GetDockablePane(paneId);
            if (pane != null)
            {
                if (pane.IsShown())
                    pane.Hide();
                else
                    pane.Show();
            }
            return Result.Succeeded;
        }
    }
}

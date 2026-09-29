using Microsoft.Extensions.Logging;

namespace NexusForever.Script.Watcher
{
    public class SourceWatcher : Watcher, ISourceWatcher
    {
        #region Dependency Injection

        public SourceWatcher(
            ILogger<ISourceWatcher> log)
            : base(log)
        {
        }

        #endregion

        /// <summary>
        /// Initialise <see cref="ISourceWatcher"/> with supplied path.
        /// </summary>
        public override void Initialise(string path)
        {
            // no LastAccess: reading the file (a grep, an editor, git) raised a reload and swapped the scripts mid-game
            var watcher = new FileSystemWatcher
            {
                Path                  = path,
                Filter                = "*.cs",
                IncludeSubdirectories = true,
                NotifyFilter          = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName
                    | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size,
            };

            watcher.Changed += RaiseEvent;
            watcher.Created += RaiseEvent;
            watcher.Deleted += RaiseEvent;
            watcher.Renamed += RaiseEvent;
            Initialise(watcher);
        }
    }
}

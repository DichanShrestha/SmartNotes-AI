using System;
using System.Web.Http;
using Hangfire;
using SmartNotesAI.Data;

namespace SmartNotesAI.Web
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        private BackgroundJobServer _backgroundJobServer;

        protected void Application_Start()
        {
            System.Web.Http.GlobalConfiguration.Configure(WebApiConfig.Register);

            // 1. Ensure EF6 Database exists
            try
            {
                using (var db = new SmartNotesDbContext())
                {
                    db.Database.CreateIfNotExists();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Database initialization note: " + ex.Message);
            }

            // 2. Initialize Hangfire Background Processing
            try
            {
                Hangfire.GlobalConfiguration.Configuration
                    .UseSqlServerStorage("DefaultConnection");

                _backgroundJobServer = new BackgroundJobServer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Hangfire initialization error: " + ex.Message);
            }
        }

        protected void Application_End()
        {
            _backgroundJobServer?.Dispose();
        }
    }
}

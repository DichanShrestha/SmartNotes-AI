using System;
using System.Web.Http;
using SmartNotesAI.Web.Security;

namespace SmartNotesAI.Web.Controllers
{
    public abstract class BaseApiController : ApiController
    {
        protected int? GetCurrentUserId()
        {
            var authHeader = Request?.Headers?.Authorization;
            if (authHeader != null && authHeader.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
            {
                return JwtHelper.ValidateAndGetUserId(authHeader.Parameter);
            }
            return null;
        }

        protected bool TryGetUserId(out int userId)
        {
            var uid = GetCurrentUserId();
            if (uid.HasValue)
            {
                userId = uid.Value;
                return true;
            }
            userId = 0;
            return false;
        }
    }
}

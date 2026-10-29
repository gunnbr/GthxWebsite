// Where the read-only API lives. Set at deploy time to the Functions app's URL, e.g.
// "https://<function-app>.azurewebsites.net/api". For local testing against `func start`, use
// "http://localhost:7071/api".
window.GTHX_API = "http://localhost:7071/api";

// The admin panel's Microsoft Entra sign-in (infra/setup-admin-signin.sh). Without a clientId, admin.html says
// sign-in isn't set up. Against `func start`, which has no App Service Authentication, set localObjectId to an
// ID in the API's Admin:AllowedObjectIds instead, and Admin:TrustPrincipalHeaders to true in its settings.
window.GTHX_ADMIN = { tenantId: "", clientId: "", localObjectId: "" };

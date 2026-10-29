using 'main.bicep'

// Values that shouldn't be in the repo come from environment variables.
param budgetContactEmail = readEnvironmentVariable('BUDGET_CONTACT_EMAIL', '')

// The admin panel's sign-in (infra/setup-admin-signin.sh). Sign-in stays off until these are set.
param adminClientId = readEnvironmentVariable('ADMIN_CLIENT_ID', '')
param adminObjectIds = readEnvironmentVariable('ADMIN_OBJECT_IDS', '')

// The bot's database (GthxNetBot's infra/main.bicepparam).
param sqlServerFqdn = 'gthxdb.database.windows.net'
param sqlDatabaseName = 'Gthx'

param allowedOrigins = [
  'https://gthx.net'
  'https://www.gthx.net'
]

param budgetStartDate = '2026-11-01'

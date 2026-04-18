namespace ChatCRM.MVC.Services
{
    public static class DotEnvLoader
    {
        public static void Load(params string[] candidatePaths)
        {
            foreach (var candidatePath in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadAllLines(candidatePath))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim().Trim('"');

                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }
            }

            MapLegacyMailVariables();
        }

        private static void MapLegacyMailVariables()
        {
            SetIfMissing("Smtp__Host", Environment.GetEnvironmentVariable("MAIL_HOST"));
            SetIfMissing("Smtp__Port", Environment.GetEnvironmentVariable("MAIL_PORT"));
            SetIfMissing("Smtp__Username", Environment.GetEnvironmentVariable("MAIL_USERNAME"));
            SetIfMissing("Smtp__Password", Environment.GetEnvironmentVariable("MAIL_PASSWORD"));
            SetIfMissing("Smtp__FromEmail", Environment.GetEnvironmentVariable("MAIL_FROM_ADDRESS"));
            SetIfMissing("Smtp__FromName", Environment.GetEnvironmentVariable("MAIL_FROM_NAME"));

            var encryption = Environment.GetEnvironmentVariable("MAIL_ENCRYPTION");
            if (!string.IsNullOrWhiteSpace(encryption) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Smtp__EnableSsl")))
            {
                var enableSsl = encryption.Equals("tls", StringComparison.OrdinalIgnoreCase) ||
                                encryption.Equals("ssl", StringComparison.OrdinalIgnoreCase);

                Environment.SetEnvironmentVariable("Smtp__EnableSsl", enableSsl ? "true" : "false");
            }
        }

        private static void SetIfMissing(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Octoshift.Models;
using OctoshiftCLI;

namespace Octoshift
{
    public class SecretScanningAlertService
    {
        private readonly GithubApi _sourceGithubApi;
        private readonly GithubApi _targetGithubApi;
        private readonly OctoLogger _log;

        public SecretScanningAlertService(GithubApi sourceGithubApi, GithubApi targetGithubApi, OctoLogger logger)
        {
            _sourceGithubApi = sourceGithubApi;
            _targetGithubApi = targetGithubApi;
            _log = logger;
        }

        public virtual async Task MigrateSecretScanningAlerts(string sourceOrg, string sourceRepo, string targetOrg,
            string targetRepo)
        {
            _log.LogInformation($"Migrating Secret Scanning Alerts from '{sourceOrg}/{sourceRepo}' to '{targetOrg}/{targetRepo}'");

            var sourceAlerts = await GetAlertsWithLocations(_sourceGithubApi, sourceOrg, sourceRepo);
            var targetAlerts = await GetAlertsWithLocations(_targetGithubApi, targetOrg, targetRepo);
            
            _log.LogInformation($"Source secret alerts found: {sourceAlerts.Count}");
            _log.LogInformation($"Target secret alerts found: {targetAlerts.Count}");
            
            _log.LogInformation("Matching secret resolutions from source to target repository");
            foreach (var alert in sourceAlerts)
            {
                _log.LogInformation($"Processing source secret {alert.Alert.Number}");
                
                if (alert.Alert.State == "resolved")
                {
                    _log.LogInformation("  secret is resolved, looking for matching detection in target...");
                    var target = MatchTargetSecret(alert, targetAlerts);

                    if (target == null)
                    {
                        _log.LogWarning($"Failed to locate a matching target secret to record a resolution for source secret {alert.Alert.Number}");
                    }
                    else
                    {
                        _log.LogInformation($"Matched alert to the target repository");

                        if (alert.Alert.Resolution == target.Alert.Resolution
                            && alert.Alert.State == target.Alert.State)
                        {
                            _log.LogSuccess("Source and Target Alerts are aligned already.");                            
                        }
                        else
                        {
                            _log.LogInformation($"Updating target alert:{target.Alert.Number} to state:{alert.Alert.State} and resolution:{alert.Alert.Resolution}");

                            await _targetGithubApi.UpdateSecretScanningAlert(targetOrg, targetRepo, target.Alert.Number,
                                alert.Alert.State, alert.Alert.Resolution);
                        
                            _log.LogSuccess($"Source and Target Alert state and resolution have been aligned.");    
                        }
                    }
                }
                else
                {
                    _log.LogInformation("  secret alert is still open, nothing to do");    
                }
                _log.LogInformation($"");
            }

            
            //TODO
            
            _log.LogSuccess("Much happy all done!");
        }

        private AlertWithLocations MatchTargetSecret(AlertWithLocations source, List<AlertWithLocations> targets)
        {
            AlertWithLocations matched = null;
            
            foreach (var target in targets)
            {
                if (matched != null)
                {
                    break;
                }

                if (source.Alert.SecretType == target.Alert.SecretType
                    && source.Alert.Secret == target.Alert.Secret)
                {
                    _log.LogVerbose($"Secret type and value match between source:{source.Alert.Number} and target:{source.Alert.Number}");
                    var locationMatch = true;
                    foreach (var sourceLocation in source.Locations)
                    {
                        locationMatch = IsMatchedSecretAlertLocation(sourceLocation, target.Locations);
                        if (!locationMatch)
                        {
                            break;
                        }
                    }

                    if (locationMatch)
                    {
                        matched = target;
                    }
                }
            }
            return matched;
        }

        private bool IsMatchedSecretAlertLocation(SecretScanningAlertLocation sourceLocation,
            SecretScanningAlertLocation[] targetLocations)
        {
            var sourceDetails = sourceLocation.Details;
            
            // We cannot guarantee the ordering of things with the locations and the APIs, typically they would match, but cannot be sure
            // so we need to iterate over all the targets to ensure a match
            foreach (var targetLocation in targetLocations)
            {
                var targetDetails = targetLocation.Details;

                if (sourceDetails.Path == targetDetails.Path
                    && sourceDetails.StartLine == targetDetails.StartLine
                    && sourceDetails.EndLine == targetDetails.EndLine
                    && sourceDetails.StartColumn == targetDetails.StartColumn
                    && sourceDetails.EndColumn == targetDetails.EndColumn
                    && sourceDetails.BlobSha == targetDetails.BlobSha)
                    // Technically this wil hold, but only if there is not commmit rewriting going on, so we need to make this last one optional
                    // && sourceDetails.CommitSha == targetDetails.CommitSha)
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<List<AlertWithLocations>> GetAlertsWithLocations(GithubApi api, string org, string repo)
        {
            var alerts = await api.GetSecretScanningAlertsForRepository(org, repo);
            var results = new List<AlertWithLocations>();
            foreach (var alert in alerts)
            {
                var locations =
                    await _sourceGithubApi.GetSecretScanningAlertsLocations(org, repo, alert.Number);
                results.Add(new AlertWithLocations { Alert = alert, Locations = locations.ToArray() });
            }
            return results;
        }
    }

    class AlertWithLocations
    {
        public SecretScanningAlert Alert { get; set; }
        
        public SecretScanningAlertLocation[] Locations { get; set; }
    }
}



using Microsoft.Azure.Batch;
using Microsoft.Azure.Batch.Auth;
using Microsoft.Azure.Batch.Common;
using Microsoft.Azure.Batch.FileStaging;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Collections.Concurrent;
using System.Text;


namespace Daenet.AzureBestPractices
{

    /// <summary>
    /// Instace that starts create pool if needed and deploys task to the Azure Batch service
    /// </summary>
    public class Deployer
    {
        #region Private members
        private ILogger<Deployer>? logger;

        /// <summary>
        /// Configuration to deploy the the batch service
        /// </summary>
        private DeployerConfig cfg;
        #endregion

        #region Constructors
        public Deployer(DeployerConfig cfg, ILogger<Deployer>? logger = null)
        {
            this.cfg = cfg;

            this.logger = logger;

        }
        #endregion

        #region Public methods


        /// <summary>
        /// This is the generall RunAsync API that can start batch job with general executable file
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="cmdArgs">Input Command Arguments to replace the Config Command Arguments the program</param>
        /// <returns></returns>
        public async Task RunAsync(string jobId, string? cmdArgs = null, bool waitToComplete = true, Action<string>? progressCallback = null)
        {
            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Deployer started. Start deploying to BatchService...", progressCallback);

            //Use config command in general case
            if (string.IsNullOrEmpty(cmdArgs))
                cmdArgs = this.cfg.CommandArguments;

            if (!File.Exists(this.cfg.ExecutableFile))
            {
                throw new FileNotFoundException($"Cannot find the Executable file in {this.cfg.ExecutableFile} for training job.");
            }

            using (BatchClient batchClient = await GetBatchClientAsync())
            {

                // Track the containers which are created as part of job submission so that we can clean them up later.
                var blobContainerNames = new List<string>();

                // Delete the blob containers which contain the task input files since we no longer need them
                CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(new StorageCredentials(this.cfg.AccountSettings.StorageAccountName,
                        this.cfg.AccountSettings.StorageAccountKey),
                        this.cfg.AccountSettings.StorageServiceUrl,
                        useHttps: true);
                try
                {
                    //Configuration for mounted Azure file share
                    AzureFileShareConfiguration afsCfg = new AzureFileShareConfiguration(this.cfg.MountSettings.AcountName, this.cfg.MountSettings.AzureFileShareURL, this.cfg.MountSettings.RelativeMountPath, this.cfg.MountSettings.AccountKey);

                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Mounting the drive:{this.cfg.MountSettings.AzureFileShareURL} to relative path: {this.cfg.MountSettings.RelativeMountPath}.", progressCallback);

                    // Allocate a pool
                    await this.CreatePoolIfNotExistAsync(batchClient, cloudStorageAccount, afsCfg, progressCallback);

                    // Submit the job
                    //jobId = string.Format("{0}-{1}-{2}", jobName, new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray()), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    //jobId = string.Format("{0}-{1}", jobName, new string(Environment.UserName.Where(char.IsLetterOrDigit).ToArray()));
                    blobContainerNames = await SubmitJobAsync(batchClient, cloudStorageAccount, jobId, cmdArgs, progressCallback);

                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Training job for model with id {jobId} is deployed. Please wait for the BatchService to start training", progressCallback);

                    // Print out the status of the pools/jobs under this account
                    //await PrintJobsAsync(batchClient);
                    //await PrintPoolsAsync(batchClient);                    

                    if (waitToComplete)
                    {
                        //// Wait for the job to complete
                        List<CloudTask> tasks = await batchClient.JobOperations.ListTasks(jobId).ToListAsync();
                        await WaitForTasksAndPrintOutputAsync(batchClient, tasks, TimeSpan.FromDays(10));
                    }
                }
                catch (Exception ex)
                {
                    await Task.Delay(20000);
                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Error, $"Error: Deployer has failed :(! {ex}");
                    throw;
                }
                finally
                {
                    if (waitToComplete)
                    {
                        // Delete the pool (if configured) and job

                        // Delete Azure Storage container data
                        await DeleteContainersAsync(cloudStorageAccount, blobContainerNames);

                        // Delete Azure Batch resources
                        List<string> jobIdsToDelete = new List<string>();
                        List<string> poolIdsToDelete = new List<string>();

                        if (this.cfg.PoolSettings.ShouldDeleteJob && jobId != null)
                        {
                            jobIdsToDelete.Add(jobId);
                        }

                        if (this.cfg.PoolSettings.ShouldDeletePool)
                        {
                            poolIdsToDelete.Add(this.cfg.PoolSettings.PoolId);
                        }

                        await DeleteBatchResourcesAsync(batchClient, jobIdsToDelete, poolIdsToDelete);
                    }
                }

            }
        }

        /// <summary>
        /// Deletes the pool from Batch service
        /// </summary>
        /// <param name="poolId">The pool id to delete.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        public async Task DeleteBatchPoolAsync(string poolId)
        {
            await DeleteBlobContainers();
            using (BatchClient batchClient = await GetBatchClientAsync())
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Deleting pool: {poolId}");
                await batchClient.PoolOperations.DeletePoolAsync(poolId).ConfigureAwait(continueOnCapturedContext: false);

            }
        }

        /// <summary>
        /// Deletes the job from Batch service
        /// </summary>
        /// <param name="jobId">The job id to delete.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        public async Task DeleteBatchJobAsync(string jobId)
        {
            using (BatchClient batchClient = await GetBatchClientAsync())
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Deleting training job for model Id: {jobId}");
                await batchClient.JobOperations.DeleteJobAsync(jobId).ConfigureAwait(continueOnCapturedContext: false);

                //await Task.Delay(1000).ConfigureAwait(continueOnCapturedContext: false);
                //LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Deleting relative Blob Container for job: {jobId}");
                //CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(new StorageCredentials(this.cfg.AccountSettings.StorageAccountName,
                //        this.cfg.AccountSettings.StorageAccountKey),
                //        this.cfg.AccountSettings.StorageServiceUrl,
                //        useHttps: true);

                //CloudBlobClient client = cloudStorageAccount.CreateCloudBlobClient();

                //var awaiterGetBlobContainersTask = client.ListContainersSegmentedAsync(new BlobContinuationToken());
                //var listBlobContainers = await awaiterGetBlobContainersTask;
                //var needDeletContainerName = listBlobContainers.Results.ToList().Where(container => container.Name.Contains(jobId)).Select(container => container.Name);
                //foreach (var containerName in needDeletContainerName)
                //{
                //    var cloudBlodContainer = client.GetContainerReference(containerName);
                //    var isDeleted = await cloudBlodContainer.DeleteIfExistsAsync();
                //    if(isDeleted)
                //        Console.WriteLine($"{containerName} is deleted");
                //    else
                //        Console.WriteLine($"{containerName} is net exist");
                //}

                //await batchClient.JobOperations.DeleteNodeFileAsync(jobId,"",.ConfigureAwait(continueOnCapturedContext: false);
            }
        }

        /// <summary>
        /// Cancels the job from Batch service
        /// </summary>
        /// <param name="jobId">The job id to cancel.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        public async Task CancelBatchJobAsync(string jobId)
        {
            using (BatchClient batchClient = await GetBatchClientAsync())
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Canceling training job for model Id: {jobId}");
                await batchClient.JobOperations.TerminateJobAsync(jobId).ConfigureAwait(continueOnCapturedContext: false);
            }
        }


        /// <summary>
        /// Get state of the Node in Batch Service
        /// </summary>
        /// <returns>State of the node</returns>
        public async Task<(ComputeNodeState? State, string Message)> GetNodeStatus()
        {
            try
            {
                var poolId = cfg.PoolSettings.PoolId;
                using (BatchClient batchClient = await GetBatchClientAsync())
                {
                    var poolStatus = await CheckPoolIfExist(poolId);
                    if (!poolStatus.Exist)
                    {
                        return (null, "PoolNotExist");
                    }

                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Trace, $"Get list of compute nodes for poolId : {poolId}");
                    var nodeList = batchClient.PoolOperations.ListComputeNodes(poolId).ToList();
                    if (nodeList.Count == 0)
                    {
                        return (null, "No node exist in given pool Id");
                    }
                    var node = await batchClient.PoolOperations.GetComputeNodeAsync(poolId, nodeList.FirstOrDefault()?.Id).ConfigureAwait(continueOnCapturedContext: false);
                    return (node.State, $"State of node: {node.State}");
                }
            }
            catch (Exception ex)
            {

                LogMessage(Microsoft.Extensions.Logging.LogLevel.Error, $"Error: Cannot get the state of the node :(!");
                return (null, ex.Message);
            }
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Delete all blob containers that store files for the tasks of pool
        /// </summary>
        /// <returns></returns>
        internal async Task DeleteBlobContainers()
        {
            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Deleting all BlobContainers use for BatchService");
            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(new StorageCredentials(this.cfg.AccountSettings.StorageAccountName,
                    this.cfg.AccountSettings.StorageAccountKey),
                    this.cfg.AccountSettings.StorageServiceUrl,
                    useHttps: true);

            CloudBlobClient blobClient = cloudStorageAccount.CreateCloudBlobClient();

            var listContainersSegment = await blobClient.ListContainersSegmentedAsync("mab", new BlobContinuationToken());
            if (listContainersSegment == null || listContainersSegment.Results.Count() == 0)
            {
                return;
            }
            foreach (var container in listContainersSegment.Results)
            {
                var blobContainer = blobClient.GetContainerReference(container.Name);
                blobContainer.DeleteIfExistsAsync().Wait();
            }
        }

        /// <summary>
        /// Extension logger to call addition action after logging
        /// </summary>
        /// <param name="level">Log level of the logg message</param>
        /// <param name="msg">meassage to log</param>
        /// <param name="progressCallback">Action to call along with the log message</param>
        private void LogMessage(Microsoft.Extensions.Logging.LogLevel level, string msg, Action<string>? progressCallback = null)
        {
            logger?.Log(level, msg);

            if (progressCallback != null)
                progressCallback(msg);
        }


        /// <summary>
        /// Check if Batch service has the pool with given poolId 
        /// </summary>
        /// <param name="poolId">Id of the pool to be checked</param>
        /// <returns>True if pool exist, false if not </returns>
        private async Task<(bool Exist, AllocationState? State)> CheckPoolIfExist(string poolId)
        {
            try
            {
                using (BatchClient batchClient = await GetBatchClientAsync())
                {
                    var pool = await batchClient.PoolOperations.GetPoolAsync(poolId);

                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Pool {poolId} exists");
                    return (true, pool.AllocationState);
                }
            }
            catch
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Pool {poolId} does not exist");
                logger?.LogInformation($"Pool {poolId} does not exist");
                return (false, null);
            }
        }


        /// <summary>
        /// Upload a file as a blob.
        /// </summary>
        /// <param name="container">The container to upload the blob to.</param>
        /// <param name="filePath">The path of the file to upload.</param>
        private async Task UploadFileToBlobAsync(CloudBlobContainer container, string filePath, Action<string>? progressCallback = null)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                CloudBlockBlob blob = container.GetBlockBlobReference(fileName);

                //Create the container if it doesn't exist.
                await container.CreateIfNotExistsAsync(BlobContainerPublicAccessType.Off, null, null)
                    .ConfigureAwait(continueOnCapturedContext: false); //Forbid public access
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Uploading {filePath} to {blob.Uri}", progressCallback);
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    //Upload the file to the specified container.
                    await blob.UploadFromStreamAsync(fileStream).ConfigureAwait(continueOnCapturedContext: false);
                }
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Done uploading {Path.GetFileName(filePath)}", progressCallback);
            }
            catch (AggregateException aggregateException)
            {
                //If there was an AggregateException process it and dump the useful information.
                foreach (Exception e in aggregateException.InnerExceptions)
                {
                    StorageException? storageException = e as StorageException;
                    if (storageException != null)
                    {
                        if (storageException.RequestInformation != null &&
                            storageException.RequestInformation.ExtendedErrorInformation != null)
                        {
                            StorageExtendedErrorInformation errorInfo = storageException.RequestInformation.ExtendedErrorInformation;
                            LogMessage(Microsoft.Extensions.Logging.LogLevel.Error, $"Error information. Code: {errorInfo.ErrorCode}, Message: {errorInfo.ErrorMessage}", progressCallback);

                            if (errorInfo.AdditionalDetails != null)
                            {
                                foreach (KeyValuePair<string, string> keyValuePair in errorInfo.AdditionalDetails)
                                {
                                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Error, $"Key: {keyValuePair.Key}, Value: {keyValuePair.Value}");
                                }
                            }
                        }
                    }
                }

                throw; //Rethrow on blob upload failure.
            }
        }

        /// <summary>
        /// Upload resources required for this job to Azure Storage.
        /// </summary>
        /// <param name="cloudStorageAccount">The cloud storage account to upload the file to.</param>
        /// <param name="containerName">The name of the container to upload the resources to.</param>
        /// <param name="filesToUpload">Additional files to upload.</param>
        private async Task UploadResourcesAsync(CloudStorageAccount cloudStorageAccount, string containerName, IEnumerable<string> filesToUpload, Action<string>? progressCallback = null)
        {
            containerName = containerName.ToLower(); //Force lower case because Azure Storage only allows lower case container names.
            //Console.WriteLine("Uploading resources to storage container: {0}", containerName);

            List<Task> asyncTasks = new List<Task>();
            CloudBlobClient client = cloudStorageAccount.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(containerName);

            //Upload any additional files specified.
            foreach (string fileName in filesToUpload)
            {
                asyncTasks.Add(UploadFileToBlobAsync(container, fileName, progressCallback));
            }

            await Task.WhenAll(asyncTasks).ConfigureAwait(continueOnCapturedContext: false); //Wait for all the uploads to finish.
        }

        /// <summary>
        /// Constructs a container shared access signature.
        /// </summary>
        /// <param name="cloudStorageAccount">The cloud storage account.</param>
        /// <param name="containerName">The container name to construct a SAS for.</param>
        /// <param name="permissions">The permissions to generate the SAS with.</param>
        /// <returns>The container URL with the SAS and specified permissions.</returns>
        private static string ConstructContainerSas(
            CloudStorageAccount cloudStorageAccount,
            string containerName,
            SharedAccessBlobPermissions permissions = SharedAccessBlobPermissions.Read)
        {
            //Lowercase the container name because containers must always be all lower case
            containerName = containerName.ToLower();

            CloudBlobClient client = cloudStorageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = client.GetContainerReference(containerName);

            DateTimeOffset sasStartTime = DateTime.UtcNow;
            TimeSpan sasDuration = TimeSpan.FromHours(2);
            DateTimeOffset sasEndTime = sasStartTime.Add(sasDuration);

            SharedAccessBlobPolicy sasPolicy = new SharedAccessBlobPolicy()
            {
                Permissions = permissions,
                SharedAccessExpiryTime = sasEndTime
            };

            string sasString = container.GetSharedAccessSignature(sasPolicy);
            return $"{container.Uri}{sasString}";
        }


        /// <summary>
        /// Uploads some files and creates a collection of resource file references to the blob paths.
        /// </summary>
        /// <param name="cloudStorageAccount">The cloud storage account to upload the resources to.</param>
        /// <param name="blobContainerName">The name of the blob container to upload the files to.</param>
        /// <param name="filePaths">The files to upload.</param>
        /// <returns>A collection of resource files.</returns>
        private async Task<List<ResourceFile>> UploadResourcesAndCreateResourceFileReferencesAsync(CloudStorageAccount cloudStorageAccount, string? blobContainerName, IEnumerable<string> filePaths, Action<string>? progressCallback = null)
        {
            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Upload Resources file to Azure Storage", progressCallback);

            // Upload the file for the start task to Azure Storage
            await UploadResourcesAsync(cloudStorageAccount, blobContainerName, filePaths, progressCallback).ConfigureAwait(continueOnCapturedContext: false);

            // Generate resource file references to the blob we just uploaded
            string containerSas = ConstructContainerSas(cloudStorageAccount, blobContainerName, permissions: SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List);

            List<string?> fileNames = filePaths.Select(Path.GetFileName).ToList();
            List<ResourceFile> resourceFiles = new List<ResourceFile> { ResourceFile.FromStorageContainerUrl(containerSas) };
            return resourceFiles;
        }


        /// <summary>
        /// Creates a pool if it doesn't already exist.  If the pool already exists, this method resizes it to meet the expected
        /// targets specified in settings.
        /// https://cloudspoint.xyz/create-pool-compute-nodes-run-azure-webhosting/
        /// </summary>
        /// <param name="batchClient">The BatchClient to create the pool with.</param>
        /// <param name="pool">The pool to create.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task<CreatePoolResult> CreatePoolIfNotExistAsync(BatchClient batchClient, CloudPool pool, Action<string>? progressCallback = null)
        {
            bool successfullyCreatedPool = false;

            int targetDedicatedNodeCount = pool.TargetDedicatedComputeNodes ?? 0;
            int targetLowPriorityNodeCount = pool.TargetLowPriorityComputeNodes ?? 0;
            string poolNodeVirtualMachineSize = pool.VirtualMachineSize;

            // Attempt to create the pool
            try
            {
                // Create an in-memory representation of the Batch pool which we would like to create.  We are free to modify/update 
                // this pool object in memory until we commit it to the service via the CommitAsync method.
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Attempting to create pool: {pool.Id}...", progressCallback);

                // Create the pool on the Batch Service
                await pool.CommitAsync().ConfigureAwait(continueOnCapturedContext: false);

                successfullyCreatedPool = true;
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Pool {pool.Id} is created with {targetDedicatedNodeCount} dedicated and {targetLowPriorityNodeCount} low priority {poolNodeVirtualMachineSize} nodes", progressCallback);
            }
            catch (BatchException e)
            {
                // Swallow the specific error code PoolExists since that is expected if the pool already exists
                if (e.RequestInformation?.BatchError?.Code == BatchErrorCodeStrings.PoolExists)
                {
                    // The pool already existed when we tried to create it
                    successfullyCreatedPool = false;
                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Warning, "The pool is already existed when we tried to create it", progressCallback);
                }
                else
                {
                    throw; // Any other exception is unexpected
                }
            }

            // If the pool already existed, make sure that its targets are correct
            if (!successfullyCreatedPool)
            {
                CloudPool existingPool = await batchClient.PoolOperations.GetPoolAsync(pool.Id).ConfigureAwait(continueOnCapturedContext: false);

                // If the pool doesn't have the right number of nodes, isn't resizing, and doesn't have
                // automatic scaling enabled, then we need to ask it to resize
                if ((existingPool.CurrentDedicatedComputeNodes != targetDedicatedNodeCount || existingPool.CurrentLowPriorityComputeNodes != targetLowPriorityNodeCount) &&
                    existingPool.AllocationState != AllocationState.Resizing &&
                    existingPool.AutoScaleEnabled == false)
                {
                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Resizing the pool.", progressCallback);
                    // Resize the pool to the desired target. Note that provisioning the nodes in the pool may take some time
                    await existingPool.ResizeAsync(targetDedicatedNodeCount, targetLowPriorityNodeCount).ConfigureAwait(continueOnCapturedContext: false);
                    return CreatePoolResult.ResizedExisting;
                }
                else
                {
                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Pool does not need to be resized.", progressCallback);
                    return CreatePoolResult.PoolExisted;
                }
            }

            return CreatePoolResult.CreatedNew;
        }

        /// <summary>
        /// Creates a pool if it doesn't already exist.  If the pool already exists, this method resizes it to meet the expected
        /// targets specified in settings.
        /// </summary>
        /// <param name="batchClient">The BatchClient to use when interacting with the Batch service.</param>
        /// <param name="cloudStorageAccount">The CloudStorageAccount to upload start task required files to.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task CreatePoolIfNotExistAsync(BatchClient batchClient, CloudStorageAccount cloudStorageAccount, AzureFileShareConfiguration afsCfg, Action<string>? progressCallback = null)
        {
            //ImageReference imageReference = new ImageReference(
            //   publisher: this.cfg.PoolSettings.Publisher,//"MicrosoftWindowsServer",
            //   offer: this.cfg.PoolSettings.Offer,//"WindowsServer",
            //   sku: this.cfg.PoolSettings.Sku, //"2012-R2-Datacenter-smalldisk",
            //   version: this.cfg.PoolSettings.Version);


            //"/subscriptions/0392ebea-55e0-4526-b483-ff123f899393/resourceGroups/RG-VCD-TEST/providers/Microsoft.Compute/galleries/VMImageGallery/images/vcd-pool-vm/versions/0.0.2"
            var imageReference = new ImageReference(this.cfg.PoolSettings.ImageReferenceId);

            // You can learn more about os families and versions at:
            // https://azure.microsoft.com/en-us/documentation/articles/cloud-services-guestos-update-matrix/
            CloudPool pool = batchClient.PoolOperations.CreatePool(
                poolId: this.cfg.PoolSettings.PoolId,
                targetDedicatedComputeNodes: this.cfg.PoolSettings.PoolTargetNodeCount,
                targetLowPriorityComputeNodes: 1,
                virtualMachineSize: this.cfg.PoolSettings.PoolNodeVirtualMachineSize,
                virtualMachineConfiguration: new VirtualMachineConfiguration(imageReference, this.cfg.PoolSettings.NodeAgendSkuId));

            //
            // Mounting vDrive to the the pool
            pool.MountConfiguration = new List<MountConfiguration> { new MountConfiguration(afsCfg) };

            //
            // Setting the number of tasks that can be executed in parallel in one node
            pool.TaskSlotsPerNode = this.cfg.PoolSettings.TasksPerNode;

            // Create a new start task to facilitate pool-wide file management or installation.
            // In this case, we just add a single dummy data file to the StartTask.
            //List<string?> files = new List<string?> { this.cfg.ExecutableFile };

            //List<ResourceFile> resourceFiles = await UploadResourcesAndCreateResourceFileReferencesAsync(cloudStorageAccount, this.cfg.PoolSettings.BlobContainer, files, progressCallback);

            pool.StartTask = new StartTask()
            {
                //CommandLine = @"cmd /c ""net use L: \\herthboss.file.core.windows.net\vcdtest /u:herthboss FphBnBHjZajG/acOk7JWQGUaAFX2EhSX8P9bWC4jTRpl/EpkhcNLhb4aGJHQjPnW9+6yypjy6NlViSl74lUdkg== /persistent:yes""",
                CommandLine = @"cmd /c ""wmic logicaldisk get caption""",
                //ResourceFiles = resourceFiles
            };

            await CreatePoolIfNotExistAsync(batchClient, pool, progressCallback);
        }

        /// <summary>
        /// Creates a pool if it doesn't already exist.  If the pool already exists, this method resizes it to meet the expected
        /// targets specified in settings.
        /// </summary>
        /// <param name="batchClient">The BatchClient to use when interacting with the Batch service.</param>
        /// <param name="cloudStorageAccount">The CloudStorageAccount to upload start task required files to.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
      /*  private async Task CreatePoolIfNotExistOLDAsync(BatchClient batchClient, CloudStorageAccount cloudStorageAccount, AzureFileShareConfiguration afsCfg, Action<string>? progressCallback = null)
        {
            // You can learn more about os families and versions at:
            // https://azure.microsoft.com/en-us/documentation/articles/cloud-services-guestos-update-matrix/
            CloudPool pool = batchClient.PoolOperations.CreatePool(

                poolId: this.cfg.PoolSettings.PoolId,
                targetDedicatedComputeNodes: this.cfg.PoolSettings.PoolTargetNodeCount,
                virtualMachineSize: this.cfg.PoolSettings.PoolNodeVirtualMachineSize,
                cloudServiceConfiguration: new CloudServiceConfiguration(this.cfg.PoolSettings.PoolOsFamily));

            //
            // Mounting vDrive to the the pool
            pool.MountConfiguration = new List<MountConfiguration> { new MountConfiguration(afsCfg) };

            //
            // Setting the number of tasks that can be executed in parallel in one node
            pool.TaskSlotsPerNode = this.cfg.PoolSettings.TasksPerNode;

            // Create a new start task to facilitate pool-wide file management or installation.
            // In this case, we just add a single dummy data file to the StartTask.
            //List<string?> files = new List<string?> { this.cfg.ExecutableFile };

            //List<ResourceFile> resourceFiles = await UploadResourcesAndCreateResourceFileReferencesAsync(cloudStorageAccount, this.cfg.PoolSettings.BlobContainer, files, progressCallback);

            pool.StartTask = new StartTask()
            {
                //CommandLine = @"cmd /c ""net use L: \\herthboss.file.core.windows.net\vcdtest /u:herthboss FphBnBHjZajG/acOk7JWQGUaAFX2EhSX8P9bWC4jTRpl/EpkhcNLhb4aGJHQjPnW9+6yypjy6NlViSl74lUdkg== /persistent:yes""",
                CommandLine = @"cmd /c ""wmic logicaldisk get caption""",
                //ResourceFiles = resourceFiles
            };

            await CreatePoolIfNotExistAsync(batchClient, pool, progressCallback);
        }*/

        /// <summary>
        /// Extracts the name of the container from the file staging artifacts.
        /// </summary>
        /// <param name="fileStagingArtifacts">The file staging artifacts.</param>
        /// <returns>A set containing all containers created by file staging.</returns>
        private static List<string> ExtractBlobContainerNames(ConcurrentBag<ConcurrentDictionary<Type, IFileStagingArtifact>> fileStagingArtifacts)
        {
            List<string> result = new List<string>();

            foreach (ConcurrentDictionary<Type, IFileStagingArtifact> artifactContainer in fileStagingArtifacts)
            {
                foreach (IFileStagingArtifact artifact in artifactContainer.Values)
                {
                    SequentialFileStagingArtifact? sequentialStagingArtifact = artifact as SequentialFileStagingArtifact;
                    if (sequentialStagingArtifact != null)
                    {
                        result.Add(sequentialStagingArtifact.BlobContainerCreated);
                    }
                }
            }

            return result;
        }


        /// <summary>
        /// Creates a job and adds a task to it. The task is a 
        /// custom executable which has a resource file associated with it.
        /// </summary>
        /// <param name="batchClient">The BatchClient to use when interacting with the Batch service.</param>
        /// <param name="cloudStorageAccount">The storage account to upload the files to.</param>
        /// <param name="jobId">The ID of the job.</param>
        /// <returns>The set of container names containing the jobs input files.</returns>
        private async Task<List<string>> SubmitJobAsync(BatchClient batchClient, CloudStorageAccount cloudStorageAccount, string jobId, string? cmdArgs, Action<string>? progressCallback = null)
        {
            await DeleteJobIfExist(batchClient, jobId, progressCallback);

            // create an empty unbound Job
            CloudJob unboundJob = batchClient.JobOperations.CreateJob();

            unboundJob.Id = jobId;
            unboundJob.PoolInformation = new PoolInformation() { PoolId = this.cfg.PoolSettings.PoolId };

            await CommitWithRetry(unboundJob);

            List<CloudTask> tasksToRun = new List<CloudTask>();

            // generate a local file in temp directory
            //string localSampleFile = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "HelloWorld.txt");
            //File.WriteAllText(localSampleFile, "hello from Batch PoolsAndResourceFiles sample!");

            StagingStorageAccount fileStagingStorageAccount = new StagingStorageAccount(
                storageAccount: this.cfg.AccountSettings.StorageAccountName,
                storageAccountKey: this.cfg.AccountSettings.StorageAccountKey,
                blobEndpoint: cloudStorageAccount.BlobEndpoint.ToString());


            // add the files as a task dependency so they will be uploaded to storage before the task 
            // is submitted and downloaded to the node before the task starts execution.
            // FileToStage helloWorldFile = new FileToStage(localSampleFile, fileStagingStorageAccount);
            FileToStage simpleTaskFile = new FileToStage(this.cfg.ExecutableFile, fileStagingStorageAccount);


            // Create a task which requires some resource files
            CloudTask taskWithFiles = new CloudTask("training_task", cmdArgs);

            // Set up a collection of files to be staged -- these files will be uploaded to Azure Storage
            // when the tasks are submitted to the Azure Batch service.
            taskWithFiles.FilesToStage = new List<IFileStagingProvider>();

            // When this task is added via JobOperations.AddTaskAsync below, the FilesToStage are uploaded to storage once.
            // The Batch service does not automatically delete content from your storage account, so files added in this 
            // way must be manually removed when they are no longer used.
            //taskWithFiles.FilesToStage.Add(helloWorldFile);
            taskWithFiles.FilesToStage.Add(simpleTaskFile);

            tasksToRun.Add(taskWithFiles);

            var fileStagingArtifacts = new ConcurrentBag<ConcurrentDictionary<Type, IFileStagingArtifact>>();

            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Submitting job {jobId} to the Batch Service...", progressCallback);

            // Use the AddTask method which takes an enumerable of tasks for best performance, as it submits up to 100
            // tasks at once in a single request.  If the list of tasks is N where N > 100, this will correctly parallelize 
            // the requests and return when all N tasks have been added.
            await batchClient.JobOperations.AddTaskAsync(jobId, tasksToRun, fileStagingArtifacts: fileStagingArtifacts);

            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Job {jobId} has been successfully submitted to the Batch Service.");

            // Extract the names of the blob containers from the file staging artifacts
            List<string> blobContainerNames = ExtractBlobContainerNames(fileStagingArtifacts);

            return blobContainerNames;
        }

        private async Task CommitWithRetry(CloudJob unboundJob, Action<string>? progressCallback = null)
        {
            int retryCnt = 5;

            while (retryCnt > 0)
            {
                try
                {
                    // Commit Job to create it in the service
                    await unboundJob.CommitAsync();

                    break;
                }
                catch (BatchException ex)
                {
                    if (ex.Message.ToLower().Contains("conflict"))
                    {
                        if (retryCnt-- < 0)
                            throw;

                        LogMessage(Microsoft.Extensions.Logging.LogLevel.Warning, $"Remaining number of attempts for trying to commit job: {retryCnt}. Retry after 15 seconds...");

                        await Task.Delay(15000);
                    }
                    else
                        throw;
                }
            }
        }

        private async Task DeleteJobIfExist(BatchClient batchClient, string jobId, Action<string>? progressCallback = null)
        {
            try
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"Checking if the job '{jobId}' already exist...");
                var existingJob = await batchClient.JobOperations.GetJobAsync(jobId);

                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"The existing job with the id '{jobId}' has been found. Deleting...");

                await batchClient.JobOperations.DeleteJobAsync(jobId);

                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"The existing job with the id '{jobId}' has been deleted. Creating the new job...", progressCallback);
            }
            catch (BatchException e)
            {
                // Swallow the specific error code PoolExists since that is expected if the pool already exists
                if (e.RequestInformation?.BatchError?.Code == BatchErrorCodeStrings.JobNotFound)
                {
                    LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Creating the job...", progressCallback);
                }
                else
                    throw;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Lists all the pools in the Batch account.
        /// </summary>
        /// <param name="batchClient">The BatchClient to use when interacting with the Batch service.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task PrintPoolsAsync(BatchClient batchClient, Action<string>? progressCallback = null)
        {
            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Listing Pools");

            // Using optional select clause to return only the properties of interest. Makes query faster and reduces HTTP packet size impact
            IPagedEnumerable<CloudPool> pools = batchClient.PoolOperations.ListPools(new ODATADetailLevel(selectClause: "id,state,currentDedicatedNodes,currentLowPriorityNodes,vmSize"));

            await pools.ForEachAsync(pool =>
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"State of pool {pool.Id} is {pool.State} and it has {pool.CurrentDedicatedComputeNodes} " +
                    $"dedicated nodes and {pool.CurrentLowPriorityComputeNodes} low-priority nodes of size {pool.VirtualMachineSize}");
            }).ConfigureAwait(continueOnCapturedContext: false);
        }

        /// <summary>
        /// Lists all the jobs in the Batch account.
        /// </summary>
        /// <param name="batchClient">The BatchClient to use when interacting with the Batch service.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task PrintJobsAsync(BatchClient batchClient, Action<string>? progressCallback = null)
        {
            LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, "Listing Jobs");

            IPagedEnumerable<CloudJob> jobs = batchClient.JobOperations.ListJobs(new ODATADetailLevel(selectClause: "id,state"));
            await jobs.ForEachAsync(job =>
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, $"State of job {job.Id} is {job.State}");
            }).ConfigureAwait(continueOnCapturedContext: false);

        }

        /// <summary>
        /// Waits for all tasks under the specified job to complete and then prints each task's output to the console.
        /// </summary>
        /// <param name="batchClient">The BatchClient to use when interacting with the Batch service.</param>
        /// <param name="tasks">The tasks to wait for.</param>
        /// <param name="timeout">The timeout.  After this time has elapsed if the job is not complete and exception will be thrown.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task WaitForTasksAndPrintOutputAsync(BatchClient batchClient, IEnumerable<CloudTask> tasks, TimeSpan timeout, Action<string>? progressCallback = null)
        {
            // We use the task state monitor to monitor the state of our tasks -- in this case we will wait for them all to complete.
            TaskStateMonitor taskStateMonitor = batchClient.Utilities.CreateTaskStateMonitor();

            // Wait until the tasks are in completed state.
            List<CloudTask> ourTasks = tasks.ToList();

            await taskStateMonitor.WhenAll(ourTasks, TaskState.Completed, timeout).ConfigureAwait(continueOnCapturedContext: false);

            // dump task output
            foreach (CloudTask t in ourTasks)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Task {t.Id}");

                //Read the standard out of the task
                NodeFile standardOutFile = await t.GetNodeFileAsync(Microsoft.Azure.Batch.Constants.StandardOutFileName).ConfigureAwait(continueOnCapturedContext: false);
                string standardOutText = await standardOutFile.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
                sb.AppendLine("Standard out:");
                sb.AppendLine(standardOutText);

                //Read the standard error of the task
                NodeFile standardErrorFile = await t.GetNodeFileAsync(Microsoft.Azure.Batch.Constants.StandardErrorFileName).ConfigureAwait(continueOnCapturedContext: false);
                string standardErrorText = await standardErrorFile.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
                sb.AppendLine("Standard error:");
                sb.AppendLine(standardErrorText);

                sb.AppendLine();
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Information, sb.ToString(), progressCallback);
            }
        }

        /// <summary>
        /// Deletes the specified containers
        /// </summary>
        /// <param name="storageAccount">The storage account with the containers to delete.</param>
        /// <param name="blobContainerNames">The name of the containers created for the jobs resource files.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task DeleteContainersAsync(CloudStorageAccount storageAccount, IEnumerable<string> blobContainerNames)
        {
            CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();
            foreach (string blobContainerName in blobContainerNames)
            {
                CloudBlobContainer container = cloudBlobClient.GetContainerReference(blobContainerName);

                LogMessage(Microsoft.Extensions.Logging.LogLevel.Trace, $"Deleting container: {blobContainerName}");

                await container.DeleteAsync().ConfigureAwait(continueOnCapturedContext: false);
            }
        }


        /// <summary>
        /// Deletes the pools and jobs specified.
        /// </summary>
        /// <param name="batchClient">The <see cref="BatchClient"/> to use to delete the pools and jobs</param>
        /// <param name="jobIds">The job ids to delete.</param>
        /// <param name="poolIds">The pool ids to delete.</param>
        /// <returns>An asynchronous <see cref="Task"/> representing the operation.</returns>
        private async Task DeleteBatchResourcesAsync(BatchClient batchClient, List<string> jobIds, List<string> poolIds)
        {
            // Delete the jobs
            foreach (string jobId in jobIds)
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Trace, $"Deleting job: {jobId}");
                await batchClient.JobOperations.DeleteJobAsync(jobId).ConfigureAwait(continueOnCapturedContext: false);
            }

            foreach (string poolId in poolIds)
            {
                LogMessage(Microsoft.Extensions.Logging.LogLevel.Trace, $"Deleting pool: {poolId}");
                await batchClient.PoolOperations.DeletePoolAsync(poolId).ConfigureAwait(continueOnCapturedContext: false);
            }
        }


        /// <summary>
        /// Create a client object to access to the bacht
        /// </summary>
        /// <returns></returns>
        private async Task<BatchClient> GetBatchClientAsync()
        {
            // Set up the Batch Service credentials used to authenticate with the Batch Service.
            // Not supported when deploying VM Images.
            // BatchSharedKeyCredentials credentials = new BatchSharedKeyCredentials(this.cfg.AccountSettings.BatchServiceUrl, this.cfg.AccountSettings.BatchAccountName, this.cfg.AccountSettings.BatchAccountKey);

            BatchTokenCredentials credentials = new BatchTokenCredentials(this.cfg.AccountSettings.BatchServiceUrl, await GetAuthenticationTokenAsync());

            BatchClient batchClient = BatchClient.Open(credentials);

            return batchClient;
        }

        private const string BatchResourceUri = "https://batch.core.windows.net/";


        private async Task<string> GetAuthenticationTokenAsync()
        {
            AuthenticationContext authContext = new AuthenticationContext(cfg.AuthorityUri);
            AuthenticationResult authResult = await authContext.AcquireTokenAsync(BatchResourceUri, new ClientCredential(cfg.ClientId, this.cfg.ClientKey));

            return authResult.AccessToken;
        }

        #endregion

    }
}

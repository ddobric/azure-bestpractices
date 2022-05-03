using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.AzureBestPractices
{
    /// <summary>
    /// Configuration for deploying to batch service.
    /// </summary>
    public class DeployerConfig
    {
        /// <summary>
        /// Defines the pool to be created.
        /// </summary>
        public PoolSettings? PoolSettings { get; set; }

        /// <summary>
        /// All required account settings.
        /// </summary>
        public AccountSettings? AccountSettings { get; set; }

        /// <summary>
        /// Mount drive settings.
        /// </summary>
        public MountSettings? MountSettings { get; set; }

        /// <summary>
        /// The full path of the executable that will be executed as a batch job.
        /// </summary>
        public string? ExecutableFile { get; set; }

        /// <summary>
        /// General Command Argument for using along with the ExecutableFile
        /// </summary>
        public string? CommandArguments { get; set; }
    }

    /// <summary>
    /// Azure File Share info to be mounted as virtual drive
    /// </summary>
    public class MountSettings
    {
        /// <summary>
        /// Storage account of the Virtual drive
        /// </summary>
        public string? AcountName { get; set; }

        /// <summary>
        /// URL of the File share
        /// </summary>
        public string? AzureFileShareURL { get; set; }

        /// <summary>
        /// Letter of the Drive to be mounted
        /// </summary>
        public string? RelativeMountPath { get; set; }

        /// <summary>
        /// Key for mounting the Azure File Share
        /// </summary>
        public string? AccountKey { get; set; }
    }

    /// <summary>
    /// Azure Batch Service Pool settings.
    /// </summary>
    public class PoolSettings
    {
        /// <summary>
        /// Name of the pool
        /// </summary>
        public string PoolId { get; set; }

        /// <summary>
        /// Number of nodes to be created
        /// </summary>
        public int PoolTargetNodeCount { get; set; }

        /// <summary>
        /// Number of tasks that can run concurrently on a single compute node
        /// </summary>
        public int TasksPerNode { get; set; } = 1;

        /// <summary>
        /// The number that represents the OS family to be used in the  pool
        /// See https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.batch.protocol.models.cloudserviceconfiguration.osfamily?view=azure-dotnet
        /// </summary>
        public string? PoolOsFamily { get; set; }

        /// <summary>
        /// Name of virtual machine with proper CPU and RAM size  
        /// See https://azure.microsoft.com/en-us/pricing/details/batch/windows-virtual-machines/
        /// </summary>
        public string? PoolNodeVirtualMachineSize { get; set; }

        /// <summary>
        /// Indicate whether to delete the pool after running or not
        /// </summary>
        public bool ShouldDeletePool { get; set; }

        /// <summary>
        /// Indicate whether to delete the job after running or not
        /// </summary>
        public bool ShouldDeleteJob { get; set; }

        /// <summary>
        /// Blob container that store pool file
        /// </summary>
        public string? BlobContainer { get; set; }

    }

    /// <summary>
    /// Storage account and batch account info
    /// </summary>
    public class AccountSettings
    {
        /// <summary>
        /// Storage account that linked with batch account
        /// </summary>
        public string? StorageAccountName { get; set; }

        /// <summary>
        /// Key to access to the storage
        /// </summary>
        public string? StorageAccountKey { get; set; }

        /// <summary>
        /// URL to connect to the storage
        /// </summary>
        public string? StorageServiceUrl { get; set; }
        /// <summary>
        /// Name of the batch account to run the pool
        /// </summary>
        public string? BatchAccountName { get; set; }

        /// <summary>
        /// Key for access to the account
        /// </summary>
        public string? BatchAccountKey { get; set; }

        /// <summary>
        /// URL to connect ot batch service
        /// </summary>
        public string? BatchServiceUrl { get; set; }
    }

    public class AzBatchLog
    {
        public string? StandardOutput { get; set; }
        public string? ErrorOutput { get; set; }
    }

    /// <summary>
    /// Indicate the state of create pool.
    /// </summary>
    public enum CreatePoolResult
    {
        PoolExisted,
        CreatedNew,
        ResizedExisting,
    }
}

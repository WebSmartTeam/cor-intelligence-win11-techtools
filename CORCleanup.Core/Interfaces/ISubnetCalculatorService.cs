using CORCleanup.Core.Models;

namespace CORCleanup.Core.Interfaces;

public interface ISubnetCalculatorService
{
    SubnetCalculation Calculate(string ipCidr);
    bool AreOnSameSubnet(string ip1, string ip2, string subnetMask);
}

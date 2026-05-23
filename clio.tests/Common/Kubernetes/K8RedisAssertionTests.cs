using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Common.Assertions;
using Clio.Common.Database;
using Clio.Common.Kubernetes;
using FluentAssertions;
using k8s.Models;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.Kubernetes;

[TestFixture]
[Property("Module", "Common")]
[Category("Unit")]
public class K8RedisAssertionTests{
	[Test]
	[Description("Should return success with firstAvailableDb in resolved Redis payload when discovery succeeds.")]
	public async Task ExecuteAsync_WhenDiscoverySucceeds_ShouldReturnUnifiedRedisDto() {
		// Arrange
		IKubernetesClient k8sClient = Substitute.For<IKubernetesClient>();
		IK8ServiceResolver serviceResolver = Substitute.For<IK8ServiceResolver>();
		IRedisDatabaseSelector redisDatabaseSelector = Substitute.For<IRedisDatabaseSelector>();
		k8sClient.ListDeploymentsAsync("clio-infrastructure", null).Returns(Task.FromResult(
			new V1DeploymentList {
				Items = [
					new V1Deployment {
						Metadata = new V1ObjectMeta { Name = "clio-redis" },
						Spec = new V1DeploymentSpec {
							Selector = new V1LabelSelector {
								MatchLabels = new Dictionary<string, string> {
									["app"] = "clio-redis"
								}
							}
						}
					}
				]
			}));
		k8sClient.ListPodsAsync("clio-infrastructure", "app=clio-redis").Returns(Task.FromResult(
			new V1PodList {
				Items = [
					new V1Pod {
						Metadata = new V1ObjectMeta { Name = "clio-redis-0" },
						Status = new V1PodStatus {
							Phase = "Running",
							Conditions = [
								new V1PodCondition { Type = "Ready", Status = "True" }
							]
						}
					}
				]
			}));
		serviceResolver.ResolveRedisServiceAsync("clio-infrastructure").Returns(Task.FromResult(new ServiceInfo {
			ServiceName = "clio-redis-lb",
			Host = "localhost",
			Port = 6379
		}));
		redisDatabaseSelector.FindEmptyDatabase("localhost", 6379).Returns(new RedisDatabaseSelectionResult {
			Success = true,
			DatabaseNumber = 4
		});
		K8RedisAssertion sut = new(k8sClient, serviceResolver, redisDatabaseSelector);

		// Act
		AssertionResult result = await sut.ExecuteAsync(false, false, "clio-infrastructure");

		// Assert
		result.Status.Should().Be("pass", because: "successful redis discovery should produce successful assertion");
		result.Resolved.Should().ContainKey("redis", because: "redis payload should be present on success");
		result.Resolved["redis"].Should().BeOfType<RedisAssertionResolvedDto>(
			because: "local and k8 scopes must return the same Redis DTO type");
		RedisAssertionResolvedDto redis = (RedisAssertionResolvedDto)result.Resolved["redis"];
		redis.Name.Should().Be("clio-redis", because: "discovered redis deployment name should be propagated");
		redis.Host.Should().Be("localhost", because: "resolved service host should be included in output payload");
		redis.Port.Should().Be(6379, because: "resolved service port should be included in output payload");
		redis.FirstAvailableDb.Should().Be(4,
			because: "output must include first discovered empty redis database index");
	}

	[Test]
	[Description("Should fail at RedisDiscovery when first available Redis DB cannot be resolved.")]
	public async Task ExecuteAsync_WhenFirstAvailableDbCannotBeResolved_ShouldFailAtDiscovery() {
		// Arrange
		IKubernetesClient k8sClient = Substitute.For<IKubernetesClient>();
		IK8ServiceResolver serviceResolver = Substitute.For<IK8ServiceResolver>();
		IRedisDatabaseSelector redisDatabaseSelector = Substitute.For<IRedisDatabaseSelector>();
		k8sClient.ListDeploymentsAsync("clio-infrastructure", null).Returns(Task.FromResult(
			new V1DeploymentList {
				Items = [
					new V1Deployment {
						Metadata = new V1ObjectMeta { Name = "clio-redis" },
						Spec = new V1DeploymentSpec {
							Selector = new V1LabelSelector {
								MatchLabels = new Dictionary<string, string> {
									["app"] = "clio-redis"
								}
							}
						}
					}
				]
			}));
		k8sClient.ListPodsAsync("clio-infrastructure", "app=clio-redis").Returns(Task.FromResult(
			new V1PodList {
				Items = [
					new V1Pod {
						Metadata = new V1ObjectMeta { Name = "clio-redis-0" },
						Status = new V1PodStatus {
							Phase = "Running",
							Conditions = [
								new V1PodCondition { Type = "Ready", Status = "True" }
							]
						}
					}
				]
			}));
		serviceResolver.ResolveRedisServiceAsync("clio-infrastructure").Returns(Task.FromResult(new ServiceInfo {
			ServiceName = "clio-redis-lb",
			Host = "localhost",
			Port = 6379
		}));
		redisDatabaseSelector.FindEmptyDatabase("localhost", 6379).Returns(new RedisDatabaseSelectionResult {
			Success = false,
			ErrorMessage = "No empty redis database available"
		});
		K8RedisAssertion sut = new(k8sClient, serviceResolver, redisDatabaseSelector);

		// Act
		AssertionResult result = await sut.ExecuteAsync(false, false, "clio-infrastructure");

		// Assert
		result.Status.Should().Be("fail",
			because: "first available db discovery is required to provide unified Redis assertion output");
		result.Scope.Should().Be(AssertionScope.K8, because: "k8 redis assertion failures must keep k8 scope");
		result.FailedAt.Should().Be(AssertionPhase.RedisDiscovery,
			because: "failure occurs during empty-db discovery phase");
		result.Reason.Should().Contain("No empty redis database available",
			because: "selector failure reason should be propagated for troubleshooting");
		result.Details.Should().ContainKey("host", because: "failure payload should include redis host");
		result.Details.Should().ContainKey("port", because: "failure payload should include redis port");
	}
}

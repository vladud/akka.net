#### 1.4.28 October 11 2021 ####
**Placeholder for nightlies**

#### 1.4.27 October 11 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.27 is a small release that contains some _major_ performance improvements for Akka.Remote.

**Performance Fixes**
In [RemoteActorRefProvider address paring, caching and resolving improvements](https://github.com/akkadotnet/akka.net/pull/5273) Akka.NET contributor @Zetanova introduced some major changes that make the entire `ActorPath` class much more reusable and more parse-efficient.

Our last major round of Akka.NET performance improvements in Akka.NET v1.4.25 produced the following:

```
OSVersion:                         Microsoft Windows NT 6.2.9200.0
ProcessorCount:                    16
ClockSpeed:                        0 MHZ
Actor Count:                       32
Messages sent/received per client: 200000  (2e5)
Is Server GC:                      True
Thread count:                      111

Num clients, Total [msg], Msgs/sec, Total [ms]
         1,  200000,    130634,    1531.54
         5, 1000000,    246975,    4049.20
        10, 2000000,    244499,    8180.16
        15, 3000000,    244978,   12246.39
        20, 4000000,    245159,   16316.37
        25, 5000000,    243333,   20548.09
        30, 6000000,    241644,   24830.55
```

In Akka.NET v1.4.27 those numbers now look like:

```
OSVersion:                         Microsoft Windows NT 6.2.9200.
ProcessorCount:                    16                            
ClockSpeed:                        0 MHZ                         
Actor Count:                       32                            
Messages sent/received per client: 200000  (2e5)                 
Is Server GC:                      True                          
Thread count:                      111                           
                                                                 
Num clients, Total [msg], Msgs/sec, Total [ms]                   
         1,  200000,    105043,    1904.29                       
         5, 1000000,    255494,    3914.73                       
        10, 2000000,    291843,    6853.30                       
        15, 3000000,    291291,   10299.75                       
        20, 4000000,    286513,   13961.68                       
        25, 5000000,    292569,   17090.64                       
        30, 6000000,    281492,   21315.35
```

To put these numbers in comparison, here's what Akka.NET's performance looked like as of v1.4.0:

```
Num clients (actors)    Total [msg] Msgs/sec    Total [ms]
1   200000  69736   2868.60
5   1000000 141243  7080.98
10  2000000 136771  14623.27
15  3000000 38190   78556.49
20  4000000 32401   123454.60
25  5000000 33341   149967.08
30  6000000 126093  47584.92
```


We've made Akka.Remote consistently faster, more predictable, and reduced total memory consumption significantly in the process.


You can [see the full set of changes introduced in Akka.NET v1.4.27 here](https://github.com/akkadotnet/akka.net/milestone/57?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 3 | 89 | 8 | Aaron Stannard |
| 1 | 856 | 519 | Andreas Dirnberger |
| 1 | 3 | 4 | Vadym Artemchuk |
| 1 | 261 | 233 | Gregorius Soedharmo |
| 1 | 1 | 1 | dependabot[bot] |

#### 1.4.26 September 28 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.26 is a very small release that addresses one wire format regression introduced in Akka.NET v1.4.20.

**Bug Fixes and Improvements**
* [Akka.Remote / Akka.Persistence: PrimitiveSerializers manifest backwards compatibility problem](https://github.com/akkadotnet/akka.net/issues/5279) - this could cause regressions when upgrading to Akka.NET v1.4.20 and later. We have resolved this issue in Akka.NET v1.4.26. [Please see our Akka.NET v1.4.26 upgrade advisory for details](https://getakka.net/community/whats-new/akkadotnet-v1.4-upgrade-advisories.html#upgrading-to-akkanet-v1426-from-older-versions).
* [Akka.DistributedData.LightningDb: Revert #5180, switching back to original LightningDB packages](https://github.com/akkadotnet/akka.net/pull/5286)

You can [see the full set of changes introduced in Akka.NET v1.4.26 here](https://github.com/akkadotnet/akka.net/milestone/57?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 4 | 99 | 96 | Gregorius Soedharmo |
| 3 | 79 | 5 | Aaron Stannard |
| 1 | 1 | 1 | dependabot[bot] |

#### 1.4.25 September 08 2021 ####
**Maintenance Release for Akka.NET 1.4**
Akka.NET v1.4.25 includes some _significant_ performance improvements for Akka.Remote and a number of important bug fixes and improvements.

**Bug Fixes and Improvements**
* [Akka.IO.Tcp: connecting to an unreachable DnsEndpoint never times out](https://github.com/akkadotnet/akka.net/issues/5154)
* [Akka.Actor: need to enforce `stdout-loglevel = off` all the way through ActorSystem lifecycle](https://github.com/akkadotnet/akka.net/issues/5246)
* [Akka.Actor: `Ask` should push unhandled answers into deadletter](https://github.com/akkadotnet/akka.net/pull/5259)
* [Akka.Routing: Make Router.Route` virtual](https://github.com/akkadotnet/akka.net/pull/5238)
* [Akka.Actor: Improve performance on `IActorRef.Child` API](https://github.com/akkadotnet/akka.net/pull/5242) - _signficantly_ improves performance of many Akka.NET functions, but includes a public API change on `IActorRef` that is source compatible but not necessarily binary-compatible. `IActorRef GetChild(System.Collections.Generic.IEnumerable<string> name)` is now `IActorRef GetChild(System.Collections.Generic.IReadOnlyList<string> name)`. This API is almost never called directly by user code (it's almost always called via the internals of the `ActorSystem` when resolving `ActorSelection`s or remote messages) so this change should be safe.
* [Akka.Actor: `IsNobody` throws NRE](https://github.com/akkadotnet/akka.net/issues/5213)
* [Akka.Cluster.Tools: singleton fix cleanup of overdue _removed members](https://github.com/akkadotnet/akka.net/pull/5229)
* [Akka.DistributedData: ddata exclude `Exiting` members in Read/Write `MajorityPlus`](https://github.com/akkadotnet/akka.net/pull/5227)

**Performance Improvements**
Using our standard `RemotePingPong` benchmark, the difference between v1.4.24 and v1.4.24 is significant:

_v1.4.24_

```
OSVersion:                         Microsoft Windows NT 6.2.9200.0 
ProcessorCount:                    16                              
ClockSpeed:                        0 MHZ                           
Actor Count:                       32                              
Messages sent/received per client: 200000  (2e5)                   
Is Server GC:                      True                            
Thread count:                      111                             
                                                                   
Num clients, Total [msg], Msgs/sec, Total [ms]                     
         1,  200000,     96994,    2062.08                         
         5, 1000000,    194818,    5133.93                         
        10, 2000000,    198966,   10052.93                         
        15, 3000000,    199455,   15041.56                         
        20, 4000000,    198177,   20184.53                         
        25, 5000000,    197613,   25302.80                         
        30, 6000000,    197349,   30403.82                         
```

_v1.4.25_

```
OSVersion:                         Microsoft Windows NT 6.2.9200.0
ProcessorCount:                    16
ClockSpeed:                        0 MHZ
Actor Count:                       32
Messages sent/received per client: 200000  (2e5)
Is Server GC:                      True
Thread count:                      111

Num clients, Total [msg], Msgs/sec, Total [ms]
         1,  200000,    130634,    1531.54
         5, 1000000,    246975,    4049.20
        10, 2000000,    244499,    8180.16
        15, 3000000,    244978,   12246.39
        20, 4000000,    245159,   16316.37
        25, 5000000,    243333,   20548.09
        30, 6000000,    241644,   24830.55
```

This represents a 24% overall throughput improvement in Akka.Remote across the board. We have additional PRs staged that should get aggregate performance improvements above 40% for Akka.Remote over v1.4.24 but they didn't make it into the Akka.NET v1.4.25 release.

You can [see the full set of changes introduced in Akka.NET v1.4.25 here](https://github.com/akkadotnet/akka.net/milestone/56?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 32 | 1301 | 400 | Aaron Stannard |
| 4 | 358 | 184 | Andreas Dirnberger |
| 3 | 414 | 149 | Gregorius Soedharmo |
| 3 | 3 | 3 | dependabot[bot] |
| 2 | 43 | 10 | zbynek001 |
| 1 | 14 | 13 | tometchy |
| 1 | 139 | 3 | carlcamilleri |

#### 1.4.24 August 17 2021 ####
**Maintenance Release for Akka.NET 1.4**

**Bug Fixes and Improvements**

* [Akka: Make `Router` open to extensions](https://github.com/akkadotnet/akka.net/pull/5201)
* [Akka: Allow null response to `Ask<T>`](https://github.com/akkadotnet/akka.net/pull/5205)
* [Akka.Cluster: Fix cluster startup race condition](https://github.com/akkadotnet/akka.net/pull/5185)
* [Akka.Streams: Fix RestartFlow bug](https://github.com/akkadotnet/akka.net/pull/5181)
* [Akka.Persistence.Sql: Implement TimestampProvider in BatchingSqlJournal](https://github.com/akkadotnet/akka.net/pull/5192)
* [Akka.Serialization.Hyperion: Bump Hyperion version from 0.11.0 to 0.11.1](https://github.com/akkadotnet/akka.net/pull/5206)
* [Akka.Serialization.Hyperion: Add Hyperion unsafe type filtering security feature](https://github.com/akkadotnet/akka.net/pull/5208)

You can [see the full set of changes introduced in Akka.NET v1.4.24 here](https://github.com/akkadotnet/akka.net/milestone/55?closed=1)

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 5 | 360 | 200 | Aaron Stannard |
| 3 | 4 | 4 | dependabot[bot] |
| 1 | 548 | 333 | Arjen Smits |
| 1 | 42 | 19 | Martijn Schoemaker |
| 1 | 26 | 27 | Andreas Dirnberger |
| 1 | 171 | 27 | Gregorius Soedharmo |

#### 1.4.23 August 09 2021 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.23 is designed to patch an issue that occurs on Linux machines using Akka.Cluster.Sharding with `akka.cluster.sharding.state-store-mode=ddata` and `akka.cluster.sharding.remember-entities=on`: "[System.DllNotFoundException: Unable to load shared library 'lmdb' or one of its dependencies](https://github.com/akkadotnet/akka.net/issues/5174)"

In [Akka.NET v1.4.21 we added built-in support for Akka.DistributedData.LightningDb](https://github.com/akkadotnet/akka.net/releases/tag/1.4.21) for use with the `remember-entities` setting, but we never received any reports about this issue until shortly after v1.4.22 was released. Fundamentally, the problem was that our downstream dependency, Lightning.NET, doesn't include any of the necessary Linux native binaries in their distributions currently. So in the meantime, we've published our own "vendored" distribution of Lightning.NET to NuGet until a new official one is released that includes these binaries.

There are some other small [fixes included in Akka.NET v1.4.23 and you can read about them here](https://github.com/akkadotnet/akka.net/milestone/54).

| COMMITS | LOC+ | LOC- | AUTHOR |
| --- | --- | --- | --- |
| 8 | 136 | 2803 | Aaron Stannard |
| 2 | 61 | 3 | Gregorius Soedharmo |

#### 1.4.22 August 05 2021 ####
**Maintenance Release for Akka.NET 1.4**

Akka.NET v1.4.22 is a fairly large release that includes an assortment of performance and bug fixes.

**Performance Fixes**
Akka.NET v1.4.22 includes a _significant_ performance improvement for `Ask<T>`, which now requires 1 internal `await` operation instead of 3:

*Before*

|                        Method | Iterations |      Mean |     Error |    StdDev |     Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------------ |----------- |----------:|----------:|----------:|----------:|------:|------:|----------:|
| RequestResponseActorSelection |      10000 | 83.313 ms | 0.7553 ms | 0.7065 ms | 4666.6667 |     - |     - |     19 MB |
|          CreateActorSelection |      10000 |  5.572 ms | 0.1066 ms | 0.1140 ms |  953.1250 |     - |     - |      4 MB |


*After*

|                        Method | Iterations |      Mean |     Error |    StdDev |     Gen 0 | Gen 1 | Gen 2 | Allocated |
|------------------------------ |----------- |----------:|----------:|----------:|----------:|------:|------:|----------:|
| RequestResponseActorSelection |      10000 | 71.216 ms | 0.9885 ms | 0.9246 ms | 4285.7143 |     - |     - |     17 MB |
|          CreateActorSelection |      10000 |  5.462 ms | 0.0495 ms | 0.0439 ms |  953.1250 |     - |     - |      4 MB |

**Bug Fixes and Improvements**

* [Akka: Use ranged nuget versioning for Newtonsoft.Json](https://github.com/akkadotnet/akka.net/pull/5099)
* [Akka: Pipe of Canceled Tasks](https://github.com/akkadotnet/akka.net/pull/5123)
* [Akka: CircuitBreaker's Open state should return a faulted Task instead of throwing](https://github.com/akkadotnet/akka.net/issues/5117)
* [Akka.Remote: Can DotNetty socket exception include information about the address?](https://github.com/akkadotnet/akka.net/issues/5130)
* [Akka.Remote: log full exception upon deserialization failure](https://github.com/akkadotnet/akka.net/pull/5121)
* [Akka.Cluster: SBR fix & update](https://github.com/akkadotnet/akka.net/pull/5147)
* [Akka.Streams: Restart Source|Flow|Sink: Configurable stream restart deadline](https://github.com/akkadotnet/akka.net/pull/5122)
* [Akka.DistributedData: ddata replicator stops but doesn't look like it can be restarted easily](https://github.com/akkadotnet/akka.net/pull/5145)
* [Akka.DistributedData: ddata ReadMajorityPlus and WriteMajorityPlus](https://github.com/akkadotnet/akka.net/pull/5146)
* [Akka.DistributedData: DData Max-Delta-Elements may not be fully honoured](https://github.com/akkadotnet/akka.net/issues/5157)

You can [see the full set of changes introduced in Akka.NET v1.4.22 here](https://github.com/akkadotnet/akka.net/milestone/52)

**Akka.Cluster.Sharding.RepairTool**
In addition to the work done on Akka.NET itself, we've also created a separate tool for cleaning up any left-over data in the event of an Akka.Cluster.Sharding cluster running with `akka.cluster.sharding.state-store-mode=persistence` was terminated abruptly before it had a chance to cleanup.

We've added documentation to the Akka.NET website that explains how to use this tool here: https://getakka.net/articles/clustering/cluster-sharding.html#cleaning-up-akkapersistence-shard-state

And the tool itself has documentation here: https://github.com/petabridge/Akka.Cluster.Sharding.RepairTool

| COMMITS | LOC+ | LOC- | AUTHOR |       
| --- | --- | --- | --- |                
| 16 | 1254 | 160 | Gregorius Soedharmo |
| 7 | 104 | 83 | Aaron Stannard |        
| 5 | 8 | 8 | dependabot[bot] |          
| 4 | 876 | 302 | Ismael Hamed |         
| 2 | 3942 | 716 | zbynek001 |           
| 2 | 17 | 3 | Andreas Dirnberger |      
| 1 | 187 | 2 | andyfurnival |           
| 1 | 110 | 5 | Igor Fedchenko |         

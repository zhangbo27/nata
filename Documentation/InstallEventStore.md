﻿#EventStore Development Environment
## Installation
1. Download [EventStore][eventstore] for Windows.
2. Download [nssm][nssm] for Windows.
3. Install both to `C:\Program Files\EventStore`
4. Configure EventStore service:

```powershell
nssm.exe install EventStore "C:\Program Files\EventStore\EventStore.ClusterNode.exe" 
  --db "C:\ProgramData\EventStore\data" 
  --log "C:\ProgramData\EventStore\logs" 
  --run-projections All
```

## Life-cycle
1. Start EventStore
    ```
    net start eventstore
    ```
2. [Browse](http://localhost:2113/) streams as `admin` with password `changeit`.
2. Monitor Log Entries
    ```
    cat -wait "C:\ProgramData\EventStore\logs\2016-06-04\127.0.0.1-2113-cluster-node.log"
    ```
3. Stop EventStore
    ```
    net stop eventstore
    ```
4. Reset Data
    ```
    rm -recurse "C:\ProgramData\EventStore\data\*"
    ```

  [eventstore]: http://geteventstore.com "EventStore Website" 
  [nssm]: https://nssm.cc "Non-Sucking Service Manager"
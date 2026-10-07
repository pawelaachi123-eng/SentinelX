package pl.sentinelx.phone;
/** Epoch and revocation rules used by the Activity and tested without Android hardware. */
public final class ConnectionLifecycle {
 private int epoch;private boolean active,blocked;private int retries;
 public synchronized int resume(boolean revoked){active=true;blocked=revoked;return ++epoch;}
 public synchronized int stop(){active=false;return ++epoch;}
 public synchronized int networkChanged(){return ++epoch;}
 public synchronized void revoke(){blocked=true;epoch++;}
 public synchronized void paired(){blocked=false;retries=0;}
 public synchronized boolean current(int candidate){return active&&!blocked&&epoch==candidate;}
 public synchronized int epoch(){return epoch;}
 public synchronized long retryDelay(int jitter){if(jitter<0||jitter>=1000)throw new IllegalArgumentException("jitter");return Math.min(60000,1000L<<Math.min(++retries,6))+jitter;}
 public synchronized void connected(){retries=0;}
}

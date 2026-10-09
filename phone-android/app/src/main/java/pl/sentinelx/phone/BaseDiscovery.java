package pl.sentinelx.phone;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
final class BaseDiscovery {
 static List<Map<String,Object>> discover() throws Exception {
  String nonce=UUID.randomUUID().toString();List<Map<String,Object>> found=new ArrayList<>();long end=System.nanoTime()+3000000000L;
  try(DatagramSocket socket=new DatagramSocket()){
   socket.setBroadcast(true);byte[] query=("SX4_DISCOVER_V421:"+nonce).getBytes(StandardCharsets.UTF_8);
   socket.send(new DatagramPacket(query,query.length,InetAddress.getByName("255.255.255.255"),42421));
   for(int count=0;count<128&&found.size()<32;count++){
    long left=(end-System.nanoTime())/1000000;if(left<=0)break;socket.setSoTimeout((int)Math.max(1,left));
    byte[] bytes=new byte[2049];DatagramPacket packet=new DatagramPacket(bytes,bytes.length);
    try{socket.receive(packet);}catch(SocketTimeoutException e){break;}
    byte[] ip=packet.getAddress().getAddress();if(ip.length!=4||!((ip[0]&255)==10||((ip[0]&255)==192&&(ip[1]&255)==168)||((ip[0]&255)==172&&(ip[1]&255)>=16&&(ip[1]&255)<=31))||packet.getLength()>2048)continue;
    try{
     Map<String,Object> data=StrictJson.object(StrictJson.parse(Sx4.utf8(Arrays.copyOf(bytes,packet.getLength()))));
     if(!"SX4".equals(data.get("protocol"))||!nonce.equals(data.get("nonce"))||!(data.get("fingerprint") instanceof String)||!((String)data.get("fingerprint")).matches("[0-9a-fA-F]{64}"))continue;
     int port=((Number)data.get("port")).intValue(),id=((Number)data.get("baseId")).intValue();if(port<1||port>65535||id<1||id>65535)continue;
     data.put("host",packet.getAddress().getHostAddress());found.add(data);
    }catch(Exception ignored){/* one malformed packet cannot end discovery */}
   }
  }
  return found;
 }
}

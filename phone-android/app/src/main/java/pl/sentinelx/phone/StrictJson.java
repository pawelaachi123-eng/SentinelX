package pl.sentinelx.phone;
import java.util.*;
import java.math.BigDecimal;

public final class StrictJson {
 private final String text;private int pos;
 private StrictJson(String text){this.text=text;}
 public static Object parse(String text) throws Sx4.Error {
  if(text.length()>16384)throw new Sx4.Error("size");StrictJson p=new StrictJson(text);
  Object value=p.value(0);p.white();if(p.pos!=text.length())throw new Sx4.Error("json");return value;
 }
 @SuppressWarnings("unchecked") public static Map<String,Object> object(Object value) throws Sx4.Error {
  if(!(value instanceof Map))throw new Sx4.Error("json");return (Map<String,Object>)value;
 }
 private void white(){while(pos<text.length()&&" \n\r\t".indexOf(text.charAt(pos))>=0)pos++;}
 private Object value(int depth) throws Sx4.Error {
  if(depth>16)throw new Sx4.Error("depth");white();if(pos>=text.length())throw new Sx4.Error("json");
  char c=text.charAt(pos);
  if(c=='{'){pos++;Map<String,Object> map=new LinkedHashMap<>();Set<String> keys=new HashSet<>();white();if(take('}'))return map;
   do{white();String key=string();if(!keys.add(key.toLowerCase(Locale.ROOT)))throw new Sx4.Error("duplicate_key");
    white();need(':');map.put(key,value(depth+1));white();if(take('}'))return map;need(',');}while(true);
  }
  if(c=='['){pos++;List<Object> list=new ArrayList<>();white();if(take(']'))return list;
   do{list.add(value(depth+1));white();if(take(']'))return list;need(',');}while(true);
  }
  if(c=='"')return string();
  for(String literal:new String[]{"true","false","null"})if(text.startsWith(literal,pos)){pos+=literal.length();return literal.equals("null")?null:Boolean.valueOf(literal);}
  int start=pos;take('-');if(!take('0')){if(pos>=text.length()||text.charAt(pos)<'1'||text.charAt(pos)>'9')throw new Sx4.Error("json");digits();}
  if(take('.')){int before=pos;digits();if(before==pos)throw new Sx4.Error("json");}
  if(take('e')||take('E')){if(!take('+'))take('-');int before=pos;digits();if(before==pos)throw new Sx4.Error("json");}
  String number=text.substring(start,pos);try{return number.indexOf('.')<0&&number.indexOf('e')<0&&number.indexOf('E')<0?Long.valueOf(number):new BigDecimal(number);}
  catch(NumberFormatException e){throw new Sx4.Error("number");}
 }
 private void digits(){while(pos<text.length()&&text.charAt(pos)>='0'&&text.charAt(pos)<='9')pos++;}
 private boolean take(char c){if(pos<text.length()&&text.charAt(pos)==c){pos++;return true;}return false;}
 private void need(char c) throws Sx4.Error {if(!take(c))throw new Sx4.Error("json");}
 private String string() throws Sx4.Error {
  need('"');StringBuilder out=new StringBuilder();
  while(pos<text.length()){
   char c=text.charAt(pos++);if(c=='"')return out.toString();if(c<32)throw new Sx4.Error("json");
   if(c=='\\'){
    if(pos==text.length())throw new Sx4.Error("json");char e=text.charAt(pos++);
    switch(e){
     case '"':case '\\':case '/':out.append(e);break;
     case 'b':out.append('\b');break;case 'f':out.append('\f');break;case 'n':out.append('\n');break;case 'r':out.append('\r');break;case 't':out.append('\t');break;
     case 'u':if(pos+4>text.length())throw new Sx4.Error("json");int v=0;for(int i=0;i<4;i++){int x=Character.digit(text.charAt(pos++),16);if(x<0)throw new Sx4.Error("json");v=(v<<4)|x;}out.append((char)v);break;
     default:throw new Sx4.Error("json");
    }
   }else out.append(c);
  }
  throw new Sx4.Error("json");
 }
 public static String encode(Object value) throws Sx4.Error {
  if(value==null)return "null";if(value instanceof Boolean)return value.toString();
  if(value instanceof Number){String s=value.toString();if(s.equals("NaN")||s.contains("Infinity"))throw new Sx4.Error("number");return s;}
  if(value instanceof String){StringBuilder b=new StringBuilder("\"");for(char c:((String)value).toCharArray()){if(c=='"'||c=='\\')b.append('\\').append(c);else if(c<32)b.append(String.format(Locale.ROOT,"\\u%04x",(int)c));else b.append(c);}return b.append('"').toString();}
  if(value instanceof Map){StringBuilder b=new StringBuilder("{");for(Map.Entry<?,?> e:((Map<?,?>)value).entrySet()){if(b.length()>1)b.append(',');if(!(e.getKey() instanceof String))throw new Sx4.Error("json");b.append(encode(e.getKey())).append(':').append(encode(e.getValue()));}return b.append('}').toString();}
  if(value instanceof Iterable){StringBuilder b=new StringBuilder("[");for(Object x:(Iterable<?>)value){if(b.length()>1)b.append(',');b.append(encode(x));}return b.append(']').toString();}
  throw new Sx4.Error("json");
 }
 public static Map<String,Object> map(Object... pairs){Map<String,Object> m=new LinkedHashMap<>();for(int i=0;i<pairs.length;i+=2)m.put((String)pairs[i],pairs[i+1]);return m;}
 private StrictJson(){text="";}
}

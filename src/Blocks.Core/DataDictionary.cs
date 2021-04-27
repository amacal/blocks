// using System;
// using System.Runtime.InteropServices;

// namespace Blocks.Core
// {
//     public class DataDictionary
//     {
//         private long offset;
//         private int size;
//         private int[] nodes;
//         private InFile inFile;
//         private InMemory inMemory;
//         private DataHasher hasher;
//         private TreeBlock[] trees;
//         private ValueFile[] files;
//         private TreeNodeBitmap bitmap;
//         private int[] seconds;

//         public DataDictionary()
//         {
//             this.size = 0;
//             this.hasher = new DataHasher();

//             this.nodes = hasher.Initialize();
//             this.bitmap = new TreeNodeBitmap();

//             this.inFile = new InFile(64 * 1024 * 1024);
//             this.inMemory = new InMemory(64 * 1024 * 1024);

//             this.offset = DateTimeOffset.Now.ToUnixTimeSeconds();
//             this.seconds = new int[3600];

//             this.files = new ValueFile[0];
//             this.trees = new TreeBlock[] { inMemory.Block(0) };
//         }

//         public int GetSize()
//         {
//             return size;
//         }

//         public string Describe()
//         {
//             long used = 0, wasted = 0;
//             long counter = 0, overhead = 0;

//             GC.Collect();
//             long memory = GC.GetTotalMemory(false);
//             long time = DateTimeOffset.Now.ToUnixTimeSeconds() - offset;

//             for (int i = 0; i < trees.Length; i++)
//             {
//                 used += trees[i].GetUsed();
//                 wasted += trees[i].GetWasted();
//             }

//             for (int i = 0; i < files.Length; i++)
//             {
//                 used += files[i].GetUsed();
//                 wasted += files[i].GetWasted();
//             }

//             for (int i = 0; i < nodes.Length; i++)
//                 while (nodes[i] > 0)
//                     counter++;

//             overhead += 4 * (long)nodes.Length;
//             overhead += Marshal.SizeOf<TreeNode>() * (long)bitmap.Size();

//             return $"{time}, size: {size}, nodes: {nodes.Length}, blocks: {trees.Length}/{files.Length}, used: {used/1048576L}M, wasted: {wasted/1048576L}M, overhead: {overhead/1048576L}M, memory: {memory/1048576L}M";
//         }

//         public ReadOnlySpan<byte> Get(byte[] key)
//         {
//             int hash = hasher.Hash(key);
//             int index = nodes[hash];

//             if (index > 0)
//             {
//                 ref TreeNode node = ref bitmap.Get(index);
//                 TreeBlock block = trees[node.Block];

//                 ValueNode link;
//                 TreeReference tree = block.GetTree(node.Offset);

//                 if (tree.TryFind(key, out link))
//                 {
//                     if (link.Block >= 0)
//                         return trees[link.Block].Extract(link);
//                     else
//                         return files[-link.Block-1].Extract(link);
//                 }
//             }

//             return null;
//         }

//         public void Set(byte[] key, byte[] value)
//         {
//             int hash = hasher.Hash(key);
//             int index = nodes[hash];

//             if (index > 0)
//             {
//                 ref TreeNode node = ref bitmap.Get(index);
//                 TreeBlock block = trees[node.Block];

//                 ValueNode link;
//                 TreeReference tree = block.GetTree(node.Offset);

//                 if (tree.TryFind(key, out link))
//                     Update(link, hash, key, value);
//                 else
//                     Insert(null, hash, key, value);
//             }

//             if (hasher.GetCapacity() <= size)
//             {
//                 hasher = hasher.Next();
//                 nodes = Rebuild();
//             }

//             if (trees.Length >= 25)
//             {
//                 int count = bitmap.Count();
//                 ValueBuilder builder = inFile.Builder();

//                 for (int i = 1; i <= count; i++)
//                 {
//                     ref TreeNode node = ref bitmap.Get(index);
//                     if (node.Block < 0) continue;

//                     TreeBlock block = trees[node.Block];
//                     TreeReference tree = block.GetTree(node.Offset);

//                     int inserted = builder.Append(tree);
//                     int next = -files.Length - 1;

//                     if (inserted == -1)
//                     {
//                         Array.Resize(ref files, files.Length + 1);
//                         files[^1] = builder.Flush();

//                         builder = inFile.Builder();
//                         inserted = builder.Append(tree);
//                         next = -files.Length - 1;
//                     }

//                     node.Block = (short)next;
//                     node.Offset = inserted;
//                 }

//                 if (builder.IsEmpty() == false)
//                 {
//                     Array.Resize(ref files, files.Length + 1);
//                     files[^1] = builder.Flush();
//                 }

//                 // ValueBlock[] rewritten = new ValueBlock[] { inMemory.Values() };
//                 // ref ValueBlock current = ref rewritten[0];

//                 // for (int i = 1; i <= count; i++)
//                 // {
//                 //     ref TreeNode node = ref bitmap.Get(index);
//                 //     if (node.Block < 0) continue;

//                 //     TreeBlock block = trees[node.Block];
//                 //     TreeReference tree = block.GetTree(node.Offset);

//                 //     byte[] data = valueBlock.Extract(node.Value);

//                 //     int inserted = current.Insert(data);
//                 //     if (inserted == -1)
//                 //     {
//                 //         Array.Resize(ref rewritten, rewritten.Length + 1);
//                 //         rewritten[^1] = inMemory.Values();

//                 //         current = ref rewritten[^1];
//                 //         inserted = current.Insert(data);
//                 //     }

//                 //     node.Value.Block = (short)(rewritten.Length - 1);
//                 //     node.Value.Offset = inserted;
//                 // }

//                 // values = rewritten;
//             }
//         }

//         private void Update(ValueNode link, int hash, byte[] key, byte[] value)
//         {
//             bool accessible = link.Block >= 0;
//             TreeBlock block = accessible ? trees[link.Block] : null;

//             if (link.Length >= value.Length)
//             {
//                 block?.Update(node.Value, value);
//                 node.Value.Length = (ushort)value.Length;
//             }
//             else
//             {
//                 valueBlock?.Remove(node.Value);
//                 Insert(index, hash, key, value);
//             }
//         }

//         private void Insert(ValueNode? link, int hash, byte[] key, byte[] value)
//         {
//             ref KeyBlock foundKeys = ref keys[0];
//             ref ValueBlock foundValues = ref values[0];
//             int positionKeys = -1, positionValues = -1;

//             for (int i = keys.Length - 1; i >= 0 && positionKeys == -1; i--)
//                 if (keys[i].GetLeft() >= key.Length)
//                     foundKeys = ref keys[positionKeys = i];

//             for (int i = values.Length - 1; i >= 0 && positionValues == -1; i--)
//                 if (values[i].GetLeft() >= value.Length)
//                     foundValues = ref values[positionValues = i];

//             if (positionKeys == -1) {
//                 Array.Resize(ref keys, keys.Length + 1);
//                 foundKeys = inMemory.Keys();
//                 positionKeys = keys.Length - 1;
//                 keys[positionKeys] = foundKeys;
//             }

//             if (positionValues == -1) {
//                 Array.Resize(ref values, values.Length + 1);
//                 foundValues = inMemory.Values();
//                 positionValues = values.Length - 1;
//                 values[positionValues] = foundValues;
//             }

//             ValueReference valueReference = new ValueReference
//             {
//                 Block = (byte)positionValues,
//                 Offset = foundValues.Insert(value),
//                 Length = (ushort)value.Length
//             };

//             if (index > 0)
//             {
//                 ref DataNode node = ref bitmap.Get(index);
//                 node.Value = valueReference;
//             }
//             else
//             {
//                 KeyReference keyReference = new KeyReference
//                 {
//                     Block = (byte)positionKeys,
//                     Offset = foundKeys.Insert(key),
//                     Length = (byte)key.Length,
//                 };

//                 int allocated = bitmap.Allocate();
//                 ref DataNode node = ref bitmap.Get(allocated);

//                 node.Fetched = 0;
//                 node.Next = nodes[hash];
//                 node.Key = keyReference;
//                 node.Value = valueReference;

//                 size = size + 1;
//                 nodes[hash] = allocated;
//             }
//         }

//         private ValueReference InsertValue(byte[] value)
//         {
//             ref ValueBlock block = ref values[0];
//             int length = value.Length, position = -1;

//             for (int i = values.Length - 1; i >= 0 && position == -1; i--)
//                 if (values[i].GetLeft() >= length)
//                     block = ref values[position = i];

//             if (position == -1) {
//                 Array.Resize(ref values, values.Length + 1);
//                 block = inMemory.Values();
//                 position = values.Length - 1;
//                 values[position] = block;
//             }

//             return new ValueReference
//             {
//                 Block = (byte)position,
//                 Offset = block.Insert(value),
//                 Length = (ushort)value.Length
//             };
//         }

//         private int[] Rebuild()
//         {
//             int[] result = hasher.Initialize();

//             for (int i = 0; i < nodes.Length; i++)
//             {
//                 int index = nodes[i];

//                 while (index > 0)
//                 {
//                     ref DataNode node = ref bitmap.Get(index);
//                     ref KeyBlock keyBlock = ref keys[node.Key.Block];
//                     int hash = keyBlock.Hash(node.Key, hasher);

//                     index = node.Next;
//                     node.Next = result[hash];
//                 }
//             }

//             return result;
//         }
//     }
// }

using System;
using System.Collections;

using SemWeb;
using SemWeb.Util;

namespace SemWeb {
	public class MemoryStore : Store, IEnumerable {
		StatementList statements = new StatementList();
		
		Hashtable statementsAboutSubject = new Hashtable();
		Hashtable statementsAboutObject = new Hashtable();
		
		bool isIndexed = false;
		internal bool allowIndexing = true;
		internal bool checkForDuplicates = false;
		
		public MemoryStore() {
		}
		
		public MemoryStore(StatementSource source) {
			Import(source);
		}
		
		public Statement[] ToArray() {
			return (Statement[])statements.ToArray(typeof(Statement));
		}

		//public IList Statements { get { return ArrayList.ReadOnly(statements); } }
		public IList Statements { get { return statements.ToArray(); } }
		  
		public override int StatementCount { get { return statements.Count; } }
		
		public Statement this[int index] {
			get {
				return statements[index];
			}
		}
		
		IEnumerator IEnumerable.GetEnumerator() {
			return statements.GetEnumerator();
		}
		
		public override void Clear() {
			statements.Clear();
			statementsAboutSubject.Clear();
			statementsAboutObject.Clear();
		}
		
		private StatementList GetIndexArray(Hashtable from, Resource entity) {
			StatementList ret = (StatementList)from[entity];
			if (ret == null) {
				ret = new StatementList();
				from[entity] = ret;
			}
			return ret;
		}
		
		public override void Add(Statement statement) {
			if (statement.AnyNull) throw new ArgumentNullException();
			if (checkForDuplicates && Contains(statement)) return;
			statements.Add(statement);
			if (isIndexed) {
				GetIndexArray(statementsAboutSubject, statement.Subject).Add(statement);
				GetIndexArray(statementsAboutObject, statement.Object).Add(statement);
			}
		}
		
		public override void Remove(Statement statement) {
			if (statement.AnyNull) {
				for (int i = 0; i < statements.Count; i++) {
					Statement s = (Statement)statements[i];
					if (statement.Matches(s)) {
						statements.RemoveAt(i); i--;
						if (isIndexed) {
							GetIndexArray(statementsAboutSubject, s.Subject).Remove(s);
							GetIndexArray(statementsAboutObject, s.Object).Remove(s);
						}
					}
				}
			} else {
				statements.Remove(statement);
				if (isIndexed) {
					GetIndexArray(statementsAboutSubject, statement.Subject).Remove(statement);
					GetIndexArray(statementsAboutObject, statement.Object).Remove(statement);
				}
			}
		}
		
		public override Entity[] GetEntities() {
			Hashtable h = new Hashtable();
			foreach (Statement s in Statements) {
				if (s.Subject != null) h[s.Subject] = h;
				if (s.Predicate != null) h[s.Predicate] = h;
				if (s.Object != null && s.Object is Entity) h[s.Object] = h;
				if (s.Meta != null && s.Meta != Statement.DefaultMeta) h[s.Meta] = h;
			}
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}
		
		public override Entity[] GetPredicates() {
			Hashtable h = new Hashtable();
			foreach (Statement s in Statements)
				h[s.Predicate] = h;
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}

		public override Entity[] GetMetas() {
			Hashtable h = new Hashtable();
			foreach (Statement s in Statements)
				h[s.Meta] = h;
			return (Entity[])new ArrayList(h.Keys).ToArray(typeof(Entity));
		}

		private void ShorterList(ref StatementList list1, StatementList list2) {
			if (list2.Count < list1.Count)
				list1 = list2;
		}
		
		public override void Select(Statement template, StatementSink result) {
			StatementList source = statements;
			
			// The first time select is called, turn indexing on for the store.
			// TODO: Perform this index in a background thread if there are a lot
			// of statements.
			if (!isIndexed && allowIndexing) {
				isIndexed = true;
				for (int i = 0; i < StatementCount; i++) {
					Statement statement = this[i];
					GetIndexArray(statementsAboutSubject, statement.Subject).Add(statement);
					GetIndexArray(statementsAboutObject, statement.Object).Add(statement);
				}
			}
			
			if (template.Subject != null) ShorterList(ref source, GetIndexArray(statementsAboutSubject, template.Subject));
			else if (template.Object != null) ShorterList(ref source, GetIndexArray(statementsAboutObject, template.Object));
			
			if (source == null) return;
			
			for (int i = 0; i < source.Count; i++) {
				Statement statement = source[i];
				if (!template.Matches(statement))
					continue;
				if (!result.Add(statement)) return;
			}
		}

		public override void Select(Entity[] subjects, Entity[] predicates, Resource[] objects, Entity[] metas, StatementSink result) {
			ResSet
				s = subjects == null ? null : new ResSet(subjects),
				p = predicates == null ? null : new ResSet(predicates),
				o = objects == null ? null : new ResSet(objects),
				m = metas == null ? null : new ResSet(metas);
				
			foreach (Statement st in statements) {
				if (s != null && !s.Contains(st.Subject)) continue;
				if (p != null && !p.Contains(st.Predicate)) continue;
				if (o != null && !o.Contains(st.Object)) continue;
				if (m != null && !m.Contains(st.Meta)) continue;
				if (!result.Add(st)) return;
			}
		}

		public override void Replace(Entity a, Entity b) {
			MemoryStore removals = new MemoryStore();
			MemoryStore additions = new MemoryStore();
			foreach (Statement statement in statements) {
				if ((statement.Subject != null && statement.Subject == a) || (statement.Predicate != null && statement.Predicate == a) || (statement.Object != null && statement.Object == a) || (statement.Meta != null && statement.Meta == a)) {
					removals.Add(statement);
					additions.Add(statement.Replace(a, b));
				}
			}
			RemoveAll(removals.ToArray());
			Import(additions);
		}
		
		public override void Replace(Statement find, Statement replacement) {
			if (find.AnyNull) throw new ArgumentNullException("find");
			if (replacement.AnyNull) throw new ArgumentNullException("replacement");
			if (find == replacement) return;
			
			foreach (Statement match in Select(find)) {
				Remove(match);
				Add(replacement);
				break; // should match just one statement anyway
			}
		}
	}
}

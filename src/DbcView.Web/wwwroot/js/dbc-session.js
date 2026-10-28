export async function load() {
    try {
        if (indexedDB.databases) {
            const databases = await indexedDB.databases();
            if (!databases.some(database => database.name === dbName)) {
                return null;
            }
        }
    } catch {
        // Fall through to open(); the request will surface a real error.
    }

    const db = await open();
    return await request(db, "readonly", store => store.get(key));
}

export async function save(json) {
    const db = await open();
    await request(db, "readwrite", store => store.put(json, key));
    return true;
}

export async function clear() {
    const db = await open();
    await request(db, "readwrite", store => store.delete(key));
    return true;
}

const dbName = "dbcview-session";
const storeName = "workspace";
const key = "snapshot";
let dbPromise = null;

function open() {
    if (!dbPromise) {
        dbPromise = new Promise((resolve, reject) => {
            const request = indexedDB.open(dbName, 1);
            request.onupgradeneeded = () => {
                if (!request.result.objectStoreNames.contains(storeName)) {
                    request.result.createObjectStore(storeName);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error ?? new Error("IndexedDB open failed"));
            request.onblocked = () => reject(new Error("IndexedDB is blocked by another tab"));
        });
    }

    return dbPromise;
}

function request(db, mode, action) {
    return new Promise((resolve, reject) => {
        const transaction = db.transaction(storeName, mode);
        const store = transaction.objectStore(storeName);
        let result;
        try {
            const operation = action(store);
            operation.onsuccess = () => {
                result = operation.result;
            };
            operation.onerror = () => reject(operation.error ?? new Error("IndexedDB request failed"));
        } catch (error) {
            reject(error);
            return;
        }

        transaction.oncomplete = () => resolve(result);
        transaction.onerror = () => reject(transaction.error ?? new Error("IndexedDB transaction failed"));
        transaction.onabort = () => reject(transaction.error ?? new Error("IndexedDB transaction aborted"));
    });
}

Saya ingin membangun thread untuk do action ai dengan ai murah, tujuannya supaya workflow itu pertama planning dilakukan oleh ai yang mahal, dan aksi nya dikerjakan oleh ai yang murah. Perbedaan dengan workflow di komunitas apa? ini benar benar membagi pekerjaan menjadi sangat kecil. Contoh ketika aku prompt ke claude, akan di arahkan ke proxy project ini, untuk diteruskan ke model ai planner yang aku set (seharusnya paling cerdas), dan ai ini di set untuk dapat menciptakan to do untuk ai executor, yang notabennya akan bekerja pada scope yang kecil, konteks yang terbatas, tapi tetap akurat. Kenapa aku bilang konteks yang terbatas tapi akurat? karena aku paham ketika ai kekurangan konteks, dia akan berhalusinasi membuat apa yang ingin dia buat. Misal di project ini sudah ada interface + service + api User, dan UI aku minta di ui di tambah kolom deskripsi, pastikan yang ditambahkan adalah field di UI, logic di JS, parameter di API, parameter di Serice, validasi di service, dan ddl di database. Dari serangkaian tugas itukan ada 7 pekerjaan, misal ai executor di command untuk buat UI untuk tambah field, dan ai executor lain di suruh js nya, kalo konteksnya kebanyakan jatuhnya boros dong, jalanin 2 ai untuk 1 task sederhana banget, cuma kalo kurang konteks pasti akan miss, misal ai executor ui buat field dengan id description tapi si ai executor js dia buat js untuk get value dari #txtDesc itu kan udah miss, atau malah ai executor js malah buat file baru js karena dia kurang kontext untuk edit file yang udah ada. Nah dari sini aku mau buat sistem untuk ai thread, dapat mengerjakan/eksekusi ai executor dalam pekerjaan sangat kecil, sehingga konsep pararel disini dapat tercipta, selesai berdasarkan siapa yang paling lambat selesai, bukan setiap waktu ai di jumlahkan.


Secara konsep aku mau provider ai nya akan dari 9router, dan akan ada menu
- setting
    digunakan untuk mengatur koneksi 9router dan key
- node
    digunakan untuk setup ai pekerja (planner/executor/wrapper)
    - wrapper bertugas untuk mewrapping output pekerjaan ketika sudah selesai, karena kan executor itu banyak, ga mungkin semua output nya akan di keluarkan semua, akan di pool dulu sampai semua selesai. dan wrapper akan membungkusnya untuk diinformasikan ke user. dan model yang ada adalah model yang tersedia pada provider yang di konekkan ke 9router + combo
- dashboard
    digunakan untuk memonitoring koneksi 9router apakah masih aktif, terus dashboard log
- project 
    digunakan untuk mendata project yang sudah terbaca oleh sistem ini, tujuannya supaya tidak hilang konteks / session kan pasti proxy project ini akan banyak di gunakan oleh project lain dalam 1 waktu yang sama, uniq nya menggunakan .sln/.slnx saja
- memory
    digunakan untuk dashboard terhadap sistem prompt yang sudah dikerjakan dan menghasilkan reuseable memory untuk tiap project, bertujuan untuk memahami kontek bisnis / teknikal lebih dalam. memory ini berisikan 2 data, 1 label ditujukan untuk keywording ai untuk mencari topik tanpa membaca full deskripsi, dan deskripsi itu sendiri, untuk isi dari topik memory yang dibuat oleh ai.

    level memory ini ada 2, short memory dan long memory, bertujuan untuk diakses oleh planner, siapa tau prompt itu baru saja di kerjakan dalam 1 hari yang sama, dan long memory yang akan didump dari short memory setelah lewat 1 hari. secara penyimpanan juga berbeda. short menggunakan redist, dan long menggunakan database.

    kemudian tipe memory akan dibagi 2, project memory dan knowledge memory, yaitu project tentu saja berkaitan dengan bisnis, validasi, flow dan lain lain tentang project itu, dan knowledge memory apa yang diprompt user, dan itu adalah sebuah fitur yang bisa digeneralkan ke project lain yang bermanfaat misal seperti get master di tampung ke redist, dan akan direfresh setiap 5 menit.

Technical
- selalu gunakan SSE untuk update apa yang terjadi di belakang ke UI
- buatkan service secara mandiri
    - NineRouterService
    - NodeService
    - ProjectService
    - MemoryService
- memanfaatkan setting.json claude untuk merubah url ke proxy ini
